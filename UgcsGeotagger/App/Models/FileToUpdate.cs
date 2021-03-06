﻿using FileParsers;
using FileParsers.SegYLog;
using FileParsers.Yaml;
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace UgCSGeotagger.Models
{
    public class FileToUpdate : DataFile
    {
        private CoveringStatus _coveringStatus = CoveringStatus.NotCovered;

        public CoveringStatus CoveringStatus
        {
            get => _coveringStatus;
            private set
            {
                this.RaiseAndSetIfChanged(ref _coveringStatus, value);
            }
        }

        public string ResultMessage { get; private set; } = "";
        public HashSet<PositioningSolutionFile> CoverageFiles { get; private set; } = new HashSet<PositioningSolutionFile>();
        public string LinkedFile { get; set; }

        public event Action<string> OnProcessingStatus;

        public FileToUpdate(string filePath, Template template) : base(filePath, template)
        {
            FindLinkedFile(filePath);
            SegyParser = new SegYLogParser(template);
        }

        public SegYLogParser SegyParser { get; private set; }

        private void FindLinkedFile(string filePath)
        {
            if (Type == FileType.Segy || Type == FileType.Unknown)
                return;
            try
            {
                var directory = Path.GetDirectoryName(filePath);
                var files = Directory.GetFiles(directory, "*.sgy");
                Regex r = new Regex(@"\d{4}-\d{2}-\d{2}-\d{2}-\d{2}-\d{2}");
                Match csvFileDateMatch = r.Match(filePath);
                if (csvFileDateMatch.Success)
                {
                    var csvFileDate = DateTime.ParseExact(csvFileDateMatch.Value, "yyyy-MM-dd-HH-mm-ss", CultureInfo.InvariantCulture);
                    foreach (var f in files)
                    {
                        Match m = r.Match(f);
                        var linkedFileDate = DateTime.ParseExact(m.Value, "yyyy-MM-dd-HH-mm-ss", CultureInfo.InvariantCulture);
                        if (linkedFileDate == csvFileDate)
                        {
                            LinkedFile = f;
                            break;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                log.Error(e.Message);
            }
        }

        public void CheckCoveringStatus(List<PositioningSolutionFile> psfFiles)
        {
            if (psfFiles.Count == 0)
                return;

            foreach (var f in psfFiles)
            {
                if ((f.StartTime <= StartTime && f.EndTime >= EndTime) || (f.StartTime <= EndTime && EndTime <= f.EndTime) || (f.StartTime <= StartTime && StartTime <= f.EndTime) || (StartTime <= f.StartTime && f.EndTime <= EndTime))
                    CoverageFiles.Add(f);
            }

            var minTime = CoverageFiles.Count > 0 ? CoverageFiles.Min(f => f.StartTime) : DateTime.Now;
            var maxTime = CoverageFiles.Count > 0 ? CoverageFiles.Max(f => f.EndTime) : DateTime.Now;
            if (CoverageFiles.Count > 0 && minTime <= StartTime && maxTime >= EndTime)
                CoveringStatus = CoveringStatus.Covered;
            else if (CoverageFiles.Count > 0)
                CoveringStatus = CoveringStatus.PartiallyCovered;
            else
                CoveringStatus = CoveringStatus.NotCovered;
        }

        public Task<string> UpdateCoordinates(CancellationTokenSource source, int timeOffset)
        {
            string message;
            if (CoverageFiles.Count == 0)
            {
                message = "Coverage files do not set";
                return Task.FromResult(message);
            }

            List<IGeoCoordinates> correctedCoordinates = new List<IGeoCoordinates>();
            var coverageCoordinates = new List<IGeoCoordinates>();
            foreach (var f in CoverageFiles)
                coverageCoordinates.AddRange(f.Coordinates);
            OnProcessingStatus?.Invoke($"Start Processing {FileName}");
            var watch = System.Diagnostics.Stopwatch.StartNew();

            var elapsedMs = watch.ElapsedMilliseconds;
            try
            {
                correctedCoordinates = Interpolator.CreateCorrectedCoordinates(Coordinates, coverageCoordinates, timeOffset, source);
                var correctedFile = CreateFileWithPreciseSuffix(FilePath);
                var result = Parser.CreateFileWithCorrectedCoordinates(FilePath, correctedFile, correctedCoordinates, source);
                message = $"{FileName}: {result.CountOfReplacedLines} of {result.CountOfLines} were replaced;";
                log.Info(message);
            }
            catch (Exception e)
            {
                message = $"Error during updating {FilePath}: {e.Message}";
                log.Error(e.Message);
            }
            finally
            {
                watch.Stop();
                OnProcessingStatus?.Invoke($"Finished Processing {FileName}, {(watch.ElapsedMilliseconds / (double)1000).ToString(CultureInfo.InvariantCulture)}s ");
            }
            if (source.IsCancellationRequested)
                return Task.FromResult(message);
            try
            {
                if (LinkedFile != null)
                {
                    watch.Reset();
                    watch.Start();
                    OnProcessingStatus?.Invoke($"Start Processing {Path.GetFileName(LinkedFile)}");
                    SegyParser.Parse(LinkedFile);
                    var correctedSegyFile = CreateFileWithPreciseSuffix(LinkedFile);
                    var result = SegyParser.CreateFileWithCorrectedCoordinates(LinkedFile, correctedSegyFile, correctedCoordinates, source);
                    message += $"\n{Path.GetFileName(LinkedFile)}: {result.CountOfReplacedLines} of {result.CountOfLines} were replaced";
                    log.Info(message);
                    return Task.FromResult(message);
                }
            }
            catch (Exception e)
            {
                message += $"\nError during updating {LinkedFile}: {e.Message}";
                log.Error(e.Message);
            }
            finally
            {
                watch.Stop();
                OnProcessingStatus?.Invoke($"End Processing {Path.GetFileName(LinkedFile)}, {(watch.ElapsedMilliseconds / (double)1000).ToString(CultureInfo.InvariantCulture)}s");
            }
            return Task.FromResult(message);
        }

        public void UnsetCoverageFile(PositioningSolutionFile file)
        {
            CoverageFiles.Remove(file);
            if (CoverageFiles.Count == 0)
                CoveringStatus = CoveringStatus.NotCovered;
        }

        private string CreateFileWithPreciseSuffix(string fullPath)
        {
            if (!File.Exists(fullPath))
                throw new FileNotFoundException($"File {fullPath} does not exist");
            var newName = $"{fullPath.Insert(fullPath.LastIndexOf("."), $"-{Precise.ToLower()}")}";
            if (File.Exists(newName))
                File.Delete(newName);
            return newName;
        }

        public int CalculateCountOfLines()
        {
            if (LinkedFile != null)
            {
                var segyLinesCount = SegyParser.Parse(LinkedFile).Count;
                return segyLinesCount + Coordinates.Count;
            }
            return Coordinates.Count;
        }
    }

    public enum CoveringStatus
    {
        Covered,
        NotCovered,
        PartiallyCovered
    }
}
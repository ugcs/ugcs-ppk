﻿using App.ViewModels;
using Avalonia.Collections;
using Avalonia.Controls;
using FileParsers;
using FileParsers.Yaml;
using log4net;
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reactive.Concurrency;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using UgCSGeotagger.Models;
using UgCSGeotagger.Views;
using YamlDotNet.Serialization;

namespace UgCSGeotagger.ViewModels
{
    public class GeotaggerToolViewModel : ViewModelBase
    {
        private static readonly ILog log = LogManager.GetLogger(typeof(GeotaggerToolViewModel));
        private const string PositioningSolutionFilesTemplatesFolder = "./Mapping/PSFTemplates";
        private const string FilesToUpdateTemplatesFolder = "./Mapping/FTUTemplates";
        private string lastOpenedFolder = "";
        private bool isDialogOpen = false;
        private readonly Deserializer deserializer = new Deserializer();
        private readonly ObservableCollection<PositioningSolutionFile> positioningSolutionFiles = new ObservableCollection<PositioningSolutionFile>();
        private readonly ObservableCollection<FileToUpdate> filesToUpdate = new ObservableCollection<FileToUpdate>();
        private readonly List<Template> psfTemplates = new List<Template>();
        private readonly List<Template> ftuTemplates = new List<Template>();
        private readonly ObservableCollection<string> messages = new ObservableCollection<string>();
        private CancellationTokenSource source;
        private int totalProgressBarValue;
        public DataGridCollectionView FilesToUpdate { get; }

        public DataGridCollectionView PositionSolutionFiles { get; }
        public DataGridCollectionView Messages { get; }

        private bool _isProcessFiles = false;

        public bool IsProcessFiles
        {
            get => _isProcessFiles;
            set
            {
                this.RaiseAndSetIfChanged(ref _isProcessFiles, value);
            }
        }

        private bool _isLongProcess = false;

        public bool IsLongProcess
        {
            get => _isLongProcess;
            set
            {
                this.RaiseAndSetIfChanged(ref _isLongProcess, value);
            }
        }

        private bool _isPsfFilesEmpty = true;

        public bool IsPsfFilesEmpty
        {
            get => _isPsfFilesEmpty;
            set
            {
                this.RaiseAndSetIfChanged(ref _isPsfFilesEmpty, value);
            }
        }

        private bool _isFtuFilesEmpty = true;

        public bool IsFtuFilesEmpty
        {
            get => _isFtuFilesEmpty;
            set
            {
                this.RaiseAndSetIfChanged(ref _isFtuFilesEmpty, value);
            }
        }

        private double _updatingFileProgressBarValue = 0.00;

        public double UpdatingFileProgressBarValue
        {
            get => _updatingFileProgressBarValue;
            set
            {
                this.RaiseAndSetIfChanged(ref _updatingFileProgressBarValue, value);
            }
        }

        private string _timeOffset = "0";

        public string TimeOffset
        {
            get => _timeOffset;
            set
            {
                this.RaiseAndSetIfChanged(ref _timeOffset, value);
            }
        }

        public GeotaggerToolViewModel()
        {
            PositionSolutionFiles = new DataGridCollectionView(positioningSolutionFiles);
            var dataGridSortDescription = DataGridSortDescription.FromPath("StartTime");
            PositionSolutionFiles.SortDescriptions.Add(dataGridSortDescription);
            positioningSolutionFiles.CollectionChanged += OnPsfFilesChanged;
            filesToUpdate.CollectionChanged += OnFtuFilesChanged;
            FilesToUpdate = new DataGridCollectionView(filesToUpdate);
            FilesToUpdate.SortDescriptions.Add(dataGridSortDescription);
            Messages = new DataGridCollectionView(messages);
            CreateTemplates();
        }

        private void OnPsfFilesChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            IsPsfFilesEmpty = positioningSolutionFiles.Count == 0;
        }

        private void OnFtuFilesChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            IsFtuFilesEmpty = filesToUpdate.Count == 0;
        }

        private async void ChooseFiles(string type)
        {
            if (isDialogOpen)
                return;
            isDialogOpen = true;
            OpenFileDialog openDialog = new OpenFileDialog() { AllowMultiple = true, Directory = lastOpenedFolder };
            openDialog.Filters.Add(new FileDialogFilter() { Name = "Data files", Extensions = { "pos", "csv", "log", "sgy" } });
            openDialog.Filters.Add(new FileDialogFilter() { Name = "All", Extensions = { "*" } });
            var chosenFiles = await openDialog.ShowAsync(new Window());
            AddFiles(chosenFiles, type);
            isDialogOpen = false;
        }

        public void AddFiles(IEnumerable<string> files, string fileType)
        {
            if (files != null)
            {
                IsLongProcess = true;
                foreach (var file in files)
                {
                    Template template = fileType switch
                    {
                        DataFile.FileToUpdateAbbr => FindTemplate(ftuTemplates, file),
                        DataFile.PositionSolutionFileAbbr => FindTemplate(psfTemplates, file),
                        _ => null,
                    };
                    if (template != null)
                    {
                        DataFile dataFile;
                        switch (fileType)
                        {
                            case DataFile.FileToUpdateAbbr:
                                dataFile = new FileToUpdate(file, template);
                                filesToUpdate.Add(dataFile as FileToUpdate);
                                break;

                            case DataFile.PositionSolutionFileAbbr:
                                dataFile = new PositioningSolutionFile(file, template);
                                positioningSolutionFiles.Add(dataFile as PositioningSolutionFile);
                                break;

                            default:
                                return;
                        }
                        foreach (var f in filesToUpdate)
                        {
                            f.CheckCoveringStatus(positioningSolutionFiles.ToList());
                        }
                    }
                    else
                        messages.Add($"Template for {file} was not found or {file} being used by another process");
                }
                GetLastOpenedDirectory(files.FirstOrDefault() ?? "");
                IsLongProcess = false;
            }
        }

        private void RemovePositioningSolutionFile()
        {
            foreach (var psf in positioningSolutionFiles.ToList())
            {
                if (psf.IsSelected)
                {
                    foreach (var ftu in filesToUpdate)
                    {
                        if (ftu.CoverageFiles.Contains(psf))
                            ftu.UnsetCoverageFile(psf);
                    }
                    PositionSolutionFiles.Remove(psf);
                }
            }

            foreach (var f in filesToUpdate)
            {
                f.CheckCoveringStatus(positioningSolutionFiles.ToList());
            }
        }

        private void RemoveFileToUpdate()
        {
            foreach (var ftu in filesToUpdate.ToList())
                if (ftu.IsSelected)
                    FilesToUpdate.Remove(ftu);
        }

        private void Clear()
        {
            filesToUpdate.Clear();
            positioningSolutionFiles.Clear();
        }

        private async void BrowseFolder()
        {
            if (isDialogOpen)
                return;
            isDialogOpen = true;
            IsLongProcess = true;
            OpenFolderDialog openDialog = new OpenFolderDialog
            {
                Directory = ""
            };
            var folder = await openDialog.ShowAsync(new Window());
            string[] files;
            if (folder != null)
            {
                try
                {
                    files = Directory.GetFiles(folder);
                }
                catch (Exception e)
                {
                    log.Error(e.Message);
                    isDialogOpen = false;
                    return;
                }
                foreach (var file in files)
                {
                    try
                    {
                        var template = FindTemplate(psfTemplates.Union(ftuTemplates).ToList(), file);
                        if (template?.FileType == FileType.ColumnsFixedWidth)
                        {
                            var psf = new PositioningSolutionFile(file, template);
                            positioningSolutionFiles.Add(psf);
                        }
                        else if (template?.FileType == FileType.CSV)
                        {
                            var ftu = new FileToUpdate(file, template);
                            filesToUpdate.Add(ftu);
                        }
                        else
                        {
                            messages.Add($"Template for {file} was not found");
                        }
                        foreach (var f in filesToUpdate)
                        {
                            f.CheckCoveringStatus(positioningSolutionFiles.ToList());
                        }
                    }
                    catch (Exception e)
                    {
                        log.Error(e.Message);
                    }
                }
                GetLastOpenedDirectory(folder);
            }
            IsLongProcess = false;
            isDialogOpen = false;
        }

        private void CreateTemplates()
        {
            var nonValidTemplates = new List<string>();
            if (!Directory.Exists(PositioningSolutionFilesTemplatesFolder))
            {
                log.Info($"Directory is not existing: {PositioningSolutionFilesTemplatesFolder[2..]}");
                return;
            }
            string[] files = new string[0];
            try
            {
                files = Directory.GetFiles(PositioningSolutionFilesTemplatesFolder, "*.yaml");
            }
            catch (Exception e)
            {
                log.Error(e.Message);
            }

            foreach (var file in files)
            {
                try
                {
                    var data = File.ReadAllText(file);
                    var tempalte = deserializer.Deserialize<Template>(data);
                    if (tempalte.IsTemplateValid())
                        psfTemplates.Add(tempalte);
                    else
                    {
                        log.Info($"Template is not valid: {file}");
                        nonValidTemplates.Add(file);
                    }
                }
                catch (Exception e)
                {
                    log.Error(e.Message);
                    nonValidTemplates.Add(file);
                }
            }

            if (!Directory.Exists(FilesToUpdateTemplatesFolder))
            {
                log.Info($"Directory is not existing: {FilesToUpdateTemplatesFolder[2..]}");
                return;
            }
            try
            {
                files = Directory.GetFiles(FilesToUpdateTemplatesFolder, "*.yaml");
            }
            catch (Exception e)
            {
                log.Error(e.Message);
            }
            foreach (var file in files)
            {
                try
                {
                    var data = File.ReadAllText(file);
                    var tempalte = deserializer.Deserialize<Template>(data);
                    if (tempalte.IsTemplateValid())
                        ftuTemplates.Add(tempalte);
                    else
                    {
                        log.Info($"Template is not valid: {file}");
                        nonValidTemplates.Add(file);
                    }
                }
                catch (Exception e)
                {
                    log.Error($"Template is not valid: {e.Message}");
                    nonValidTemplates.Add(file);
                }
            }
            ftuTemplates.Add(CreateSegyTemplate());
            messages.Add($"Valid Templates: {ftuTemplates.Count + psfTemplates.Count}, Invalid Templates: {nonValidTemplates.Count}");
            foreach (var t in nonValidTemplates)
                messages.Add($"Template {t} is not valid");
        }

        public Template FindTemplate(List<Template> templates, string file)
        {
            if (file.EndsWith(".sgy"))
                return templates.First(t => t.FileType == FileType.Segy);
            foreach (var t in templates)
            {
                try
                {
                    var firstNonEmptyLines = File.ReadLines(file).Take(10).ToList();
                    foreach (var l in firstNonEmptyLines)
                    {
                        var regex = new Regex(t.MatchRegex);
                        if (regex.IsMatch(l))
                            return t;
                    }
                }
                catch (Exception e)
                {
                    log.Error(e.Message);
                }
            }
            return null;
        }

        private void GetLastOpenedDirectory(string file)
        {
            try
            {
                lastOpenedFolder = Path.GetDirectoryName(file);
            }
            catch (Exception e)
            {
                log.Error(e.Message);
                lastOpenedFolder = "";
            }
        }

        private async void ProcessFiles()
        {
            if (IsProcessFiles)
                CancelProcessing();
            else
            {
                var isTimeOffsetCorrect = int.TryParse(TimeOffset, out int timeOffset);
                if (!isTimeOffsetCorrect)
                {
                    await MessageBoxView.Show(App.App.CurrentWindow, "Time Offset is incorrect", "Error", MessageBoxView.MessageBoxButtons.Ok);
                    return;
                }

                UpdatingFileProgressBarValue = 0.00;
                IsProcessFiles = true;
                source = new CancellationTokenSource();
                totalProgressBarValue = 0;
                try
                {
                    foreach (var ftu in filesToUpdate)
                    {
                        if (ftu.CoveringStatus != CoveringStatus.NotCovered)
                            totalProgressBarValue += ftu.CalculateCountOfLines();
                    }
                }
                catch (Exception e)
                {
                    await MessageBoxView.Show(App.App.CurrentWindow, e.Message, "Error", MessageBoxView.MessageBoxButtons.Ok);
                    return;
                }
                foreach (var ftu in filesToUpdate)
                {
                    if (ftu.CoveringStatus != CoveringStatus.NotCovered)
                    {
                        totalProgressBarValue = ftu.Coordinates.Count;
                        ftu.Parser.OnOneHundredLinesReplaced += UpdateProgressbar;
                        Interpolator.OnOneHundredLinesReplaced += UpdateProgressbar;
                        ftu.SegyParser.OnOneHundredLinesReplaced += UpdateProgressbar;
                        ftu.OnProcessingStatus += AddMessage;
                        var message = await Task.Run(() => ftu.UpdateCoordinates(source, timeOffset));
                        ftu.OnProcessingStatus -= AddMessage;
                        Interpolator.OnOneHundredLinesReplaced -= UpdateProgressbar;
                        ftu.Parser.OnOneHundredLinesReplaced -= UpdateProgressbar;
                        ftu.SegyParser.OnOneHundredLinesReplaced -= UpdateProgressbar;
                        await MessageBoxView.Show(App.App.CurrentWindow, message, "Info", MessageBoxView.MessageBoxButtons.Ok);
                    }
                }
                IsProcessFiles = false;
            }
        }

        private void AddMessage(string message)
        {
            RxApp.MainThreadScheduler.Schedule(() =>
            {
                messages.Add(message);
            });
        }

        private void UpdateProgressbar(int value)
        {
            UpdatingFileProgressBarValue = value / (double)totalProgressBarValue * 100;
        }

        private Template CreateSegyTemplate()
        {
            return new Template()
            {
                Code = "Segy",
                FileType = FileType.Segy,
                Name = "Segy"
            };
        }

        public void CancelProcessing()
        {
            source.Cancel();
            source.Dispose();
            IsProcessFiles = false;
            UpdatingFileProgressBarValue = 0.00;
        }
    }
}
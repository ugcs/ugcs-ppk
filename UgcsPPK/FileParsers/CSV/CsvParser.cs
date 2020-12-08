﻿using FileParsers.Exceptions;
using FileParsers.Yaml;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;

namespace FileParsers.CSV
{
    public class CsvParser : Parser
    {
        public CsvParser(Template template) : base(template)
        {
        }

        public override List<IGeoCoordinates> Parse(string logPath)
        {
            if (!File.Exists(logPath))
                throw new FileNotFoundException("File {logPath} does not exist");
            if (Template == null)
                throw new NullReferenceException($"Template is not set");
            if (Template.DataMapping.Date?.Source == Yaml.Data.Source.FileName)
                ParseDateFromNameOfFile(logPath);
            var coordinates = new List<IGeoCoordinates>();
            using (StreamReader reader = File.OpenText(logPath))
            {
                string line = SkipLines(reader);
                if (Template.Format.HasHeader)
                {
                    if (line == null)
                        line = reader.ReadLine();
                    FindIndexesByHeaders(line);
                    skippedLines.Append(line + "\n");
                }
                format = new CultureInfo("en-US", false);
                format.NumberFormat.NumberDecimalSeparator = Template.Format.DecimalSeparator;
                var traceCount = 0;
                while ((line = reader.ReadLine()) != null)
                {
                    if (line.StartsWith(Template.Format.CommentPrefix))
                        continue;
                    var data = line.Split(new[] { Template.Format.Separator }, StringSplitOptions.None);
                    var lat = ParseDouble(Template.DataMapping.Latitude, data[(int)Template.DataMapping.Latitude.Index]);
                    var lon = ParseDouble(Template.DataMapping.Longitude, data[(int)Template.DataMapping.Longitude.Index]);
                    var traceNumber = Template.DataMapping.TraceNumber != null && Template.DataMapping.TraceNumber.Index != null ?
                        ParseInt(Template.DataMapping.TraceNumber, data[(int)Template.DataMapping.TraceNumber.Index]) : traceCount;
                    traceCount++;
                    var date = ParseDateTime(data);
                    coordinates.Add(new GeoCoordinates(date, lat, lon, traceNumber));
                }
            }
            return coordinates;
        }

        protected void ParseDateFromNameOfFile(string logName)
        {
            Regex r = new Regex(Template.DataMapping.Date.Regex);
            Match m = r.Match(logName);
            if (!m.Success)
                throw new IncorrectDateFormatException("Incorrect file name. Set date of logging.");
            dateFromNameOfFile = DateTime.ParseExact(m.Value, Template.DataMapping.Date.Format, CultureInfo.InvariantCulture);
        }

        public override Result CreatePpkCorrectedFile(string oldFile, string newFile, IEnumerable<IGeoCoordinates> coordinates, CancellationTokenSource token)
        {
            if (!File.Exists(oldFile))
                throw new FileNotFoundException("File {oldFile} does not exist");
            if (Template == null)
                throw new NullReferenceException($"Template is not set");
            var result = new Result();
            format = new CultureInfo("en-US", false);
            format.NumberFormat.NumberDecimalSeparator = Template.Format.DecimalSeparator;
            using StreamReader reader = File.OpenText(oldFile);
            string line;
            var traceCount = 0;
            var dict = coordinates.ToDictionary(k => k.TraceNumber);
            using (StreamWriter ppkFile = new StreamWriter(newFile))
            {
                line = SkipLines(reader);
                ppkFile.WriteLine(skippedLines.ToString().TrimEnd(new char[] { '\n' }));
                while ((line = reader.ReadLine()) != null)
                {
                    if (token.IsCancellationRequested)
                        break;
                    try
                    {
                        if (line.StartsWith(Template.Format.CommentPrefix))
                            continue;
                        var data = line.Split(new[] { Template.Format.Separator }, StringSplitOptions.None);
                        var traceNumber = Template.DataMapping.TraceNumber != null && Template.DataMapping.TraceNumber.Index != null ? int.Parse(data[(int)Template.DataMapping.TraceNumber.Index]) : traceCount;
                        var coordinateFound = dict.TryGetValue(traceNumber, out IGeoCoordinates coordinate);
                        if (coordinateFound)
                        {
                            data[(int)Template.DataMapping.Longitude.Index] = dict[traceNumber].Longitude.ToString(format);
                            data[(int)Template.DataMapping.Latitude.Index] = dict[traceNumber].Latitude.ToString(format);
                            ppkFile.WriteLine(string.Join(Template.Format.Separator, data));
                            result.CountOfReplacedLines++;
                        }
                    }
                    catch (Exception)
                    {
                    }
                    finally
                    {
                        result.CountOfLines++;
                        CountOfReplacedLines++;
                        traceCount++;
                    }
                }
            }
            return result;
        }

        protected void FindIndexesByHeaders(string line)
        {
            List<string> headers;
            headers = line.Split(new[] { Template.Format.Separator }, StringSplitOptions.None).ToList();
            Template.DataMapping.Latitude.Index = headers.FindIndex(h => h.Equals(Template.DataMapping.Latitude.Header));
            Template.DataMapping.Longitude.Index = headers.FindIndex(h => h.Equals(Template.DataMapping.Longitude.Header));
            if (Template.DataMapping.Date != null && Template.DataMapping.Date.Header != null)
                Template.DataMapping.Date.Index = headers.FindIndex(h => h.Equals(Template.DataMapping.Date.Header));
            if (Template.DataMapping.Time != null && Template.DataMapping.Time.Header != null)
                Template.DataMapping.Time.Index = headers.FindIndex(h => h.Equals(Template.DataMapping.Time.Header));
            if (Template.DataMapping.DateTime != null && Template.DataMapping.DateTime.Header != null)
                Template.DataMapping.DateTime.Index = headers.FindIndex(h => h.Equals(Template.DataMapping.Time.Header));
            if (Template.DataMapping.Timestamp != null && Template.DataMapping.Timestamp.Header != null)
                Template.DataMapping.Timestamp.Index = headers.FindIndex(h => h.Equals(Template.DataMapping.Timestamp.Header));
            if (Template.DataMapping.TraceNumber != null && Template.DataMapping.TraceNumber.Header != null)
                Template.DataMapping.TraceNumber.Index = headers.FindIndex(h => h.Equals(Template.DataMapping.TraceNumber.Header));

            if (Template.DataMapping.Latitude.Index == -1 || Template.DataMapping.Longitude.Index == -1)
                throw new ColumnsMatchingException("Column names are not matched");
        }
    }
}
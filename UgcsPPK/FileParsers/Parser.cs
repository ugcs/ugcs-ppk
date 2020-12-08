﻿using FileParsers.Exceptions;
using FileParsers.Yaml;
using FileParsers.Yaml.Data;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace FileParsers
{
    public abstract class Parser : IGeoCoordinateParser
    {
        protected StringBuilder skippedLines;
        protected System.DateTime? dateFromNameOfFile;
        protected CultureInfo format;
        public Template Template { get; }
        private int _countOfReplacedLines;

        public int CountOfReplacedLines
        {
            get => _countOfReplacedLines;
            set
            {
                _countOfReplacedLines = value;
                if (CountOfReplacedLines % 100 == 0)
                    OnOneHundredLinesReplaced?.Invoke(CountOfReplacedLines);
            }
        }

        public event Action<int> OnOneHundredLinesReplaced;

        public abstract List<IGeoCoordinates> Parse(string path);

        public abstract Result CreatePpkCorrectedFile(string oldFile, string newFile, IEnumerable<IGeoCoordinates> coordinates, CancellationTokenSource token);

        public Parser(Template template)
        {
            Template = template;
        }

        protected string SkipLines(StreamReader reader)
        {
            string line;
            skippedLines = new StringBuilder();
            if (Template.SkipLinesTo != null)
            {
                while ((line = reader.ReadLine()) != null)
                {
                    skippedLines.Append(line + "\n");
                    var regex = new Regex(Template.SkipLinesTo.MatchRegex);
                    if (regex.IsMatch(line))
                        break;
                }
                if (Template.SkipLinesTo.SkipMatchedLine)
                {
                    line = reader.ReadLine();
                    skippedLines.Append(line + "\n");
                    return line;
                }
                else
                    return line;
            }
            return null;
        }

        protected double ParseDouble(BaseData data, string column)
        {
            if (!string.IsNullOrEmpty(data.Regex))
            {
                var match = FindByRegex(data.Regex, column);
                if (match.Success)
                    return double.Parse(match.Value, NumberStyles.Float, format);
            }
            return double.Parse(column, NumberStyles.Float, format);
        }

        protected int ParseInt(BaseData data, string column)
        {
            if (!string.IsNullOrEmpty(data.Regex))
            {
                var match = FindByRegex(data.Regex, column);
                if (match.Success)
                    return int.Parse(match.Value);
            }
            return int.Parse(column);
        }

        protected System.DateTime ParseDateTime(string[] data)
        {
            if (Template.DataMapping.DateTime != null && Template.DataMapping.DateTime?.Index != -1)
            {
                return ParseDateAndTime(Template.DataMapping.DateTime, data[(int)Template.DataMapping.DateTime.Index]);
            }
            else if (Template.DataMapping.Time != null && Template.DataMapping.Time.Index != -1 && Template.DataMapping?.Date != null && Template.DataMapping.Date?.Index != null)
            {
                var date = ParseDateAndTime(Template.DataMapping.Date, data[(int)Template.DataMapping.Date.Index]);
                var time = ParseDateAndTime(Template.DataMapping.Time, data[(int)Template.DataMapping.Time.Index]);
                var totalMS = CalculateTotalMS(time);
                var dateTime = date.AddMilliseconds(totalMS);
                return dateTime;
            }
            else if (Template.DataMapping.Time != null && Template.DataMapping.Time?.Index != -1 && dateFromNameOfFile != null)
            {
                var time = ParseDateAndTime(Template.DataMapping.Time, data[(int)Template.DataMapping.Time.Index]);
                var totalMS = CalculateTotalMS(time);
                var dateTime = dateFromNameOfFile.Value.AddMilliseconds(totalMS);
                return dateTime;
            }
            else
                throw new IncorrectDateFormatException("Cannot parse DateTime form file");
        }

        private System.DateTime ParseDateAndTime(Yaml.Data.DateTime data, string column)
        {
            if (!string.IsNullOrEmpty(data.Regex))
            {
                var match = FindByRegex(data.Regex, column);
                if (match.Success)
                    return System.DateTime.ParseExact(match.Value, data.Format, CultureInfo.InvariantCulture);
            }
            return System.DateTime.ParseExact(column, data.Format, CultureInfo.InvariantCulture);
        }

        private int CalculateTotalMS(System.DateTime time)
        {
            return time.Second * 1000 + time.Minute * 60000 + time.Hour * 3600000 + time.Millisecond;
        }

        private Match FindByRegex(string regex, string column)
        {
            var r = new Regex(regex);
            var m = r.Match(column);
            return m;
        }
    }
}
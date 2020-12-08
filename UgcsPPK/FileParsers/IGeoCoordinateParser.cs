﻿using System.Collections.Generic;
using System.Threading;

namespace FileParsers
{
    public interface IGeoCoordinateParser
    {
        List<IGeoCoordinates> Parse(string path);

        Result CreatePpkCorrectedFile(string oldFile, string newFile, IEnumerable<IGeoCoordinates> coordinates, CancellationTokenSource token);
    }
}
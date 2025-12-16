namespace PaddleOcr;

using OpenCvSharp;

public readonly record struct DetResult(Rect Box, float Score);

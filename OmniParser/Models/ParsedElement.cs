namespace OmniParser.Models;

using System.Drawing;

public record ParsedElement(
    Rectangle Box,
    string Type,       // "text" or "icon"
    string? OcrContent,
    string? FlorenceContent,
    bool Interactivity,
    float Score
);

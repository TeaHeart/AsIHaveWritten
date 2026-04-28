namespace OmniParser.Models;

using System.Drawing;

public record ParsedElement(
    Rectangle Box,
    string Type,       // "text" or "icon"
    string? Content,   // OCR text or icon caption
    bool Interactivity,
    float Score
);

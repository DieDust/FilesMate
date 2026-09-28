namespace FilesMate.App.Models;

public enum FileTypeTone { Neutral, Blue, Teal, Amber, Violet, Rose }

/// <summary>Presentation only: never probes files or changes search/sort classification.</summary>
public readonly record struct FileTypeAppearance(string Extension, FileTypeTone Tone)
{
    public static FileTypeAppearance For(string? name, bool directory)
    {
        if (directory || string.IsNullOrEmpty(name)) return default;
        var extension = Path.GetExtension(name).TrimStart('.').ToUpperInvariant();
        if (extension.Length == 0) return default;
        var tone = extension switch
        {
            "DOC" or "DOCX" or "ODT" or "RTF" => FileTypeTone.Blue,
            "XLS" or "XLSX" or "ODS" or "CSV" or "EXE" or "COM" => FileTypeTone.Teal,
            "ZIP" or "7Z" or "RAR" or "TAR" or "GZ" or "PPT" or "PPTX" => FileTypeTone.Amber,
            "PNG" or "JPG" or "JPEG" or "GIF" or "WEBP" or "HEIC" or "SVG" or "PSD"
                or "MP4" or "MKV" or "MOV" or "WEBM" => FileTypeTone.Violet,
            "PDF" or "MP3" or "WAV" or "FLAC" or "M4A" or "OGG" => FileTypeTone.Rose,
            "JS" or "TS" or "CS" or "CPP" or "PY" or "HTML" or "CSS" or "JSON" or "XML" => FileTypeTone.Amber,
            _ => FileTypeTone.Neutral
        };
        return new(extension, tone);
    }
}

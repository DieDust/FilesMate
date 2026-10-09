using System.Xml.Linq;

namespace FilesMate.Search.Tests;

/// <summary>Source integration contracts, not WPF rendering or Windows input tests.</summary>
public sealed class SearchPresentationContractTests
{
    [Fact]
    public void CompositionStartInvalidatesOldRowsAndRefreshesBeforeAnyFurtherInput()
    {
        var code = Read("PaletteWindow.xaml.cs");
        Assert.Contains("AddPreviewTextInputStartHandler(QueryBox, Query_CompositionStarted)", code);
        var start = Between(code, "private void Query_CompositionStarted", "private void Query_CompositionCompleted");
        Assert.True(start.IndexOf("_composing = true", StringComparison.Ordinal) < start.IndexOf("CancelSearch()", StringComparison.Ordinal));
        Assert.Contains("_pending = true", start);
        Assert.Contains("_presentationFailed = false", start);
        Assert.Contains("RefreshSearchPresentation()", start);
        var complete = Between(code, "private void Query_CompositionCompleted", "private void Query_Changed");
        Assert.Contains("if (!_composing) return", complete);
        Assert.Contains("generation == _searchGeneration && !_composing", complete);
        Assert.Contains("if (!_composing) Search(); else RefreshSearchPresentation()", code);
    }

    [Fact]
    public void EverySelectionCommandSharesTheCurrentGenerationGate()
    {
        Assert.Contains("private SearchRow[] SelectedRows() => HasCurrentSearchResults ?", Read("PaletteWindow.Interactions.cs"));
        Assert.Contains("SearchPresentationPolicy.HasCurrentResults(", Read("PaletteWindow.Presentation.cs"));
        var progress = Read("PaletteWindow.SearchProgress.cs");
        Assert.Contains("if (snapshot.Generation != _searchGeneration || !IsVisible) return", progress);
        Assert.Contains("SearchPresentationPolicy.RetainSelection(", progress);
        Assert.Contains("SearchResultInteractionBusy", progress);
    }

    [Fact]
    public void CompletionRequiresATerminalResponseAndSurvivesNeitherCancelNorDismiss()
    {
        var code = Read("PaletteWindow.xaml.cs");
        Assert.Contains("receivedFinal |= update.IsFinal", code);
        Assert.Contains("if (!receivedFinal && _query == request", code);
        Assert.Contains("_presentationCompleted = false", Between(code, "public void CancelSearch", "internal void Dismiss"));
        var dismiss = Between(code, "internal void Dismiss", "private void Window_Deactivated");
        Assert.Contains("_presentationFailed = false", dismiss);
        Assert.Contains("_presentationCompleted = false", dismiss);
        Assert.Contains("SearchPresentationPolicy.SuccessfulCompletion(snapshot.IsFinal, incomplete)", Read("PaletteWindow.SearchProgress.cs"));
        Assert.Contains("SearchPresentationPolicy.ShowCompleted(state, _presentationCompleted", Read("PaletteWindow.Presentation.cs"));
    }

    [Fact]
    public void FocusRestorationUsesIdentityAndDoesNotStealNewInputFocus()
    {
        var source = Read("PaletteWindow.SearchProgress.cs");
        Assert.Contains("if (!Results.IsKeyboardFocusWithin", source);
        Assert.Contains("CaptureSearchResultFocus()", source);
        Assert.Contains("if (!snapshot.Append) RestoreSearchResultFocus(focus, snapshot.Generation)", source);
        Assert.Contains("if (focus is null || CaptureSearchResultFocus() == focus) return", source);
        Assert.Contains("SearchHitIdentity.From(row.Hit) == focus.Identity", source);
        Assert.Contains("ReferenceEquals(Keyboard.FocusedElement, fallbackFocus)", source);
        Assert.Contains("generation != _searchGeneration", source);
        Assert.DoesNotContain("Results.UpdateLayout()", source);
        Assert.Contains("RestoreResultSelection(selected)", source);
        Assert.Contains("_suppressPreviewSelection = suppressed", source);
    }

    [Fact]
    public void MotionUsesSharedTimingsAndGateWithoutScalingOrFlashingTheList()
    {
        var motion = Read("PaletteWindow.Motion.cs");
        Assert.Contains("ReduceMotionGate.AnimationsEnabled", motion);
        Assert.Contains("!SystemParameters.HighContrast", motion);
        Assert.Contains("MotionDurations.Fast", motion);
        Assert.Contains("MotionDurations.Standard", motion);
        Assert.Contains("new DoubleAnimation(4, 0, duration)", motion);
        Assert.DoesNotContain("ScaleTransform", motion);
        Assert.DoesNotContain("AnimateResultsChanged", motion);
        Assert.DoesNotContain("TimeSpan.FromMilliseconds(120)", motion);
        Assert.DoesNotContain("TimeSpan.FromMilliseconds(180)", motion);
        Assert.Contains("ResetSearchAnimations()", Between(Read("PaletteWindow.xaml.cs"), "public void CancelSearch", "internal void Dismiss"));
    }

    [Fact]
    public void ThemeSurfacesAndAccentActionsShareMainPaletteAndSystemContrastPairs()
    {
        var appearance = Read("PaletteAppearance.cs");
        Assert.Contains("SkinPalette.For(dark, settings)", appearance);
        Assert.Contains("skin.CompanionColors()", appearance);
        Assert.Contains("resources[\"Accent\"] = SystemColors.HighlightBrush", appearance);
        Assert.Contains("resources[\"OnAccent\"] = SystemColors.HighlightTextBrush", appearance);
        Assert.Contains("resources[\"SelectedInk\"] = resources[\"SelectedMuted\"] = SystemColors.HighlightTextBrush", appearance);
        Assert.Contains("settings.EffectiveTransparencyPercent", appearance);
        Assert.Contains("GlassMaterialPolicy.ContentCoverage", appearance);
    }

    [Fact]
    public void CompactDesignKeepsLogoFilenamePathAndReservedActions()
    {
        var xaml = XDocument.Parse(Read("PaletteWindow.xaml"));
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        XElement Named(string name) => xaml.Descendants().Single(element => (string?)element.Attribute(x + "Name") == name);
        Assert.Equal("24", (string?)Named("SearchIcon").Attribute("Width"));
        Assert.Equal("Assets/FilesMate.png", (string?)Named("SearchIcon").Attribute("Source"));
        Assert.Equal("15", (string?)Named("ResultName").Attribute("FontSize"));
        Assert.NotNull(Named("ResultName").Attributes().Single(attribute => attribute.Name.LocalName == "SearchMatchText.Text"));
        var row = Named("ResultRowBody");
        Assert.Contains(row.Descendants(), element => element.Name.LocalName == "TextBlock" && (string?)element.Attribute("Text") == "{Binding Location}");
        Assert.Contains(row.Descendants(), element => element.Name.LocalName == "ColumnDefinition" && (string?)element.Attribute("Width") == "92");
        Assert.Equal("4", (string?)Named("ResultActionStrip").Attribute("Grid.Column"));
        Assert.NotNull(Named("SearchEmptyCard"));
        Assert.NotNull(Named("SearchRetryButton"));
        Assert.NotNull(Named("SelectedRail"));
    }

    [Fact]
    public void OriginalDpiAndDeferredCenteringGuardsRemainInPlace()
    {
        var code = Read("PaletteWindow.xaml.cs");
        Assert.Contains("ReferenceEquals(e.OriginalSource, this)", code);
        Assert.Contains("_queuedCenter is { Status: DispatcherOperationStatus.Pending }", code);
        Assert.Contains("_centerAfterGeneration = true", code);
        Assert.Contains("if (_centeringPalette || handle == IntPtr.Zero", code);
        var dismiss = Between(code, "internal void Dismiss", "private void Window_Deactivated");
        Assert.Contains("CancelQueuedCenter()", dismiss);
        Assert.Contains("CancelSearch()", dismiss);
    }

    private static string Between(string source, string start, string end)
    {
        var from = source.IndexOf(start, StringComparison.Ordinal);
        return source[from..source.IndexOf(end, from, StringComparison.Ordinal)];
    }

    private static string Read(string name)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var file = Path.Combine(directory.FullName, "src", "FilesMate.SearchHost", name);
            if (File.Exists(file)) return File.ReadAllText(file);
        }
        throw new DirectoryNotFoundException("The SearchHost source root was not found.");
    }
}

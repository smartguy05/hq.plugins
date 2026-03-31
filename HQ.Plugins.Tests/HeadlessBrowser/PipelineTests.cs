using HQ.Plugins.HeadlessBrowser.Pipeline;

namespace HQ.Plugins.Tests.HeadlessBrowser;

public class AriaSnapshotExtractorTests
{
    private const string SampleYaml = """
        - navigation "Main Menu":
          - link "Home"
          - link "Products"
          - link "About"
        - main:
          - heading "Welcome" [level=1]
          - paragraph: Some introductory text here
          - form "Contact":
            - textbox "Email"
            - textbox "Message"
            - button "Send"
        - contentinfo:
          - link "Privacy Policy"
        """;

    [Fact]
    public void IsUsable_WithValidYaml_ReturnsTrue()
    {
        Assert.True(AriaSnapshotExtractor.IsUsable(SampleYaml));
    }

    [Fact]
    public void IsUsable_WithEmptyString_ReturnsFalse()
    {
        Assert.False(AriaSnapshotExtractor.IsUsable(""));
        Assert.False(AriaSnapshotExtractor.IsUsable(null));
        Assert.False(AriaSnapshotExtractor.IsUsable("  \n  \n  "));
    }

    [Fact]
    public void IsUsable_WithTooFewLines_ReturnsFalse()
    {
        Assert.False(AriaSnapshotExtractor.IsUsable("- heading \"Title\"\n- link \"Home\""));
    }

    [Fact]
    public void Truncate_WithinLimit_ReturnsUnchanged()
    {
        var result = AriaSnapshotExtractor.Truncate(SampleYaml, 100);
        Assert.Equal(SampleYaml, result);
    }

    [Fact]
    public void Truncate_ExceedsLimit_TruncatesWithMessage()
    {
        var result = AriaSnapshotExtractor.Truncate(SampleYaml, 5);
        Assert.Contains("more lines", result);
        Assert.True(result.Split('\n').Length < SampleYaml.Split('\n').Length);
    }

    [Fact]
    public void ParseInteractiveElements_FindsAllInteractive()
    {
        var elements = AriaSnapshotExtractor.ParseInteractiveElements(SampleYaml);

        Assert.Contains(elements, e => e.Role == "link" && e.Name == "Home");
        Assert.Contains(elements, e => e.Role == "link" && e.Name == "Products");
        Assert.Contains(elements, e => e.Role == "textbox" && e.Name == "Email");
        Assert.Contains(elements, e => e.Role == "textbox" && e.Name == "Message");
        Assert.Contains(elements, e => e.Role == "button" && e.Name == "Send");
        Assert.Contains(elements, e => e.Role == "link" && e.Name == "Privacy Policy");
    }

    [Fact]
    public void ParseInteractiveElements_IgnoresNonInteractive()
    {
        var elements = AriaSnapshotExtractor.ParseInteractiveElements(SampleYaml);

        Assert.DoesNotContain(elements, e => e.Role == "heading");
        Assert.DoesNotContain(elements, e => e.Role == "navigation");
        Assert.DoesNotContain(elements, e => e.Role == "main");
    }
}

public class RefAssignerTests
{
    private const string SampleYaml = """
        - navigation:
          - link "Home"
          - link "About"
        - main:
          - textbox "Search"
          - button "Go"
        """;

    [Fact]
    public void Assign_AssignsSequentialRefIds()
    {
        var snapshot = RefAssigner.Assign(SampleYaml, "https://example.com");

        Assert.Equal(4, snapshot.RefMap.Count);
        Assert.Contains("e1", snapshot.RefMap.Keys);
        Assert.Contains("e2", snapshot.RefMap.Keys);
        Assert.Contains("e3", snapshot.RefMap.Keys);
        Assert.Contains("e4", snapshot.RefMap.Keys);
    }

    [Fact]
    public void Assign_AnnotatesYamlWithRefs()
    {
        var snapshot = RefAssigner.Assign(SampleYaml, "https://example.com");

        Assert.Contains("[ref=e1]", snapshot.AnnotatedYaml);
        Assert.Contains("[ref=e2]", snapshot.AnnotatedYaml);
        Assert.Contains("[ref=e3]", snapshot.AnnotatedYaml);
        Assert.Contains("[ref=e4]", snapshot.AnnotatedYaml);
    }

    [Fact]
    public void Assign_PreservesOriginalYaml()
    {
        var snapshot = RefAssigner.Assign(SampleYaml, "https://example.com");

        Assert.Equal(SampleYaml, snapshot.Yaml);
        Assert.NotEqual(SampleYaml, snapshot.AnnotatedYaml);
    }

    [Fact]
    public void Assign_StoresCorrectElementInfo()
    {
        var snapshot = RefAssigner.Assign(SampleYaml, "https://example.com");

        var e1 = snapshot.RefMap["e1"];
        Assert.Equal("link", e1.Role);
        Assert.Equal("Home", e1.Name);

        var e3 = snapshot.RefMap["e3"];
        Assert.Equal("textbox", e3.Role);
        Assert.Equal("Search", e3.Name);
    }
}

public class OutlineBuilderTests
{
    [Fact]
    public void Build_KeepsHeadingsAndNavigation()
    {
        var yaml = """
            - banner:
              - navigation "Main":
                - link "Home" [ref=e1]
            - main:
              - heading "Title" [level=1]
              - paragraph: lots of text content here
              - heading "Section" [level=2]
              - paragraph: more content
              - form "Login":
                - textbox "Email" [ref=e2]
                - button "Submit" [ref=e3]
            """;

        var outline = OutlineBuilder.Build(yaml);

        Assert.Contains("navigation", outline);
        Assert.Contains("heading", outline);
        Assert.Contains("link", outline);
        Assert.Contains("form", outline);
        Assert.Contains("textbox", outline);
        Assert.Contains("button", outline);
        Assert.DoesNotContain("paragraph", outline);
    }

    [Fact]
    public void Build_ReturnsEmptyForEmptyInput()
    {
        Assert.Equal(string.Empty, OutlineBuilder.Build(""));
        Assert.Equal(string.Empty, OutlineBuilder.Build(null));
    }
}

public class PageSearcherTests
{
    private const string SampleYaml = """
        - heading "Welcome to Our Store"
        - link "Electronics" [ref=e1]
        - link "Clothing" [ref=e2]
        - heading "Featured Products"
        - text "Best laptop deal of 2024"
        - link "Buy Now" [ref=e3]
        """;

    [Fact]
    public void Search_FindsMatchingLines()
    {
        var result = PageSearcher.Search(SampleYaml, "laptop");

        Assert.Contains("laptop", result);
        Assert.Contains("1 match", result);
    }

    [Fact]
    public void Search_CaseInsensitive()
    {
        var result = PageSearcher.Search(SampleYaml, "ELECTRONICS");

        Assert.Contains("Electronics", result);
    }

    [Fact]
    public void Search_ReturnsNotFoundMessage()
    {
        var result = PageSearcher.Search(SampleYaml, "nonexistent");

        Assert.Contains("No matches found", result);
    }

    [Fact]
    public void Search_IncludesContext()
    {
        var result = PageSearcher.Search(SampleYaml, "Featured");

        // Should include surrounding lines as context
        Assert.Contains("Featured Products", result);
    }
}

public class TaskFilterTests
{
    private const string SampleYaml = """
        - banner:
          - navigation "Main":
            - link "Home"
        - main:
          - heading "Login"
          - form "Login Form":
            - textbox "Username"
            - textbox "Password"
            - button "Sign In"
          - table:
            - row: Data row 1
        - contentinfo:
          - link "Footer Link"
        """;

    [Fact]
    public void Filter_FormFill_KeepsFormElements()
    {
        var result = TaskFilter.Filter(SampleYaml, "form_fill");

        Assert.Contains("textbox", result);
        Assert.Contains("button", result);
        Assert.Contains("heading", result);
    }

    [Fact]
    public void Filter_Navigation_KeepsLinks()
    {
        var result = TaskFilter.Filter(SampleYaml, "navigation");

        Assert.Contains("link", result);
        Assert.Contains("heading", result);
        Assert.Contains("navigation", result);
    }

    [Fact]
    public void Filter_DataExtraction_KeepsContent()
    {
        var result = TaskFilter.Filter(SampleYaml, "data_extraction");

        Assert.Contains("table", result);
        Assert.Contains("heading", result);
    }

    [Fact]
    public void Filter_General_ReturnsUnchanged()
    {
        var result = TaskFilter.Filter(SampleYaml, "general");

        Assert.Equal(SampleYaml, result);
    }

    [Fact]
    public void Filter_NullHint_ReturnsUnchanged()
    {
        var result = TaskFilter.Filter(SampleYaml, null);

        Assert.Equal(SampleYaml, result);
    }
}

public class DiffEngineTests
{
    [Fact]
    public void ComputeDiff_IdenticalSnapshots_NoChanges()
    {
        var yaml = """
            - heading "Title" [ref=e1]
            - link "Home" [ref=e2]
            """;

        var snapshot = RefAssigner.Assign(yaml, "https://example.com");
        var delta = DiffEngine.ComputeDiff(snapshot, snapshot);

        Assert.False(DiffEngine.IsSignificant(delta));
    }

    [Fact]
    public void ComputeDiff_AddedElements_DetectsAdditions()
    {
        var yaml1 = """
            - heading "Title" [ref=e1]
            - link "Home" [ref=e2]
            """;

        var yaml2 = """
            - heading "Title" [ref=e1]
            - link "Home" [ref=e2]
            - button "New Button" [ref=e3]
            """;

        var snap1 = RefAssigner.Assign(yaml1, "https://example.com");
        var snap2 = RefAssigner.Assign(yaml2, "https://example.com");
        var delta = DiffEngine.ComputeDiff(snap1, snap2);

        Assert.True(DiffEngine.IsSignificant(delta));
        Assert.NotEmpty(delta.Added);
    }

    [Fact]
    public void ComputeDiff_ChangedElement_DetectsChange()
    {
        var yaml1 = """
            - heading "Step 1" [ref=e1]
            - button "Next" [ref=e2]
            """;

        var yaml2 = """
            - heading "Step 2" [ref=e1]
            - button "Next" [ref=e2]
            """;

        var snap1 = RefAssigner.Assign(yaml1, "https://example.com");
        var snap2 = RefAssigner.Assign(yaml2, "https://example.com");
        var delta = DiffEngine.ComputeDiff(snap1, snap2);

        Assert.True(DiffEngine.IsSignificant(delta));
        Assert.NotEmpty(delta.Changed);
        Assert.Contains(delta.Changed, c => c.Ref == "e1");
    }

    [Fact]
    public void ComputeDiff_NullPrevious_ReturnsNull()
    {
        var yaml = "- heading \"Title\" [ref=e1]";
        var snapshot = RefAssigner.Assign(yaml, "https://example.com");

        Assert.Null(DiffEngine.ComputeDiff(null, snapshot));
    }
}

public class DomCompressorTests
{
    [Fact]
    public void Compress_CollapsesWrapperDivs()
    {
        var root = new DomNode
        {
            Tag = "div",
            Children = new List<DomNode>
            {
                new()
                {
                    Tag = "div", // Wrapper with single child
                    Children = new List<DomNode>
                    {
                        new() { Tag = "p", Children = new List<DomNode>
                        {
                            new() { IsText = true, TextContent = "Hello" }
                        }}
                    }
                }
            }
        };

        var result = DomCompressor.Compress(root);

        // The intermediate div should be collapsed
        Assert.Equal("p", result.Tag);
    }

    [Fact]
    public void Compress_PreservesSemanticNodes()
    {
        var root = new DomNode
        {
            Tag = "div",
            Id = "main-content",
            Children = new List<DomNode>
            {
                new() { Tag = "div", Children = new List<DomNode>
                {
                    new() { IsText = true, TextContent = "Content" }
                }}
            }
        };

        var result = DomCompressor.Compress(root);

        // Should preserve the div with id
        Assert.Equal("main-content", result.Id);
    }

    [Fact]
    public void Serialize_ProducesReadableOutput()
    {
        var node = new DomNode
        {
            Tag = "form",
            Role = "form",
            Children = new List<DomNode>
            {
                new() { Tag = "input", InputType = "text", Placeholder = "Name" },
                new() { Tag = "button", Children = new List<DomNode>
                {
                    new() { IsText = true, TextContent = "Submit" }
                }}
            }
        };

        var output = DomCompressor.Serialize(node);

        Assert.Contains("<form [role=form]>", output);
        Assert.Contains("<input [type=text, placeholder=\"Name\"]>", output);
        Assert.Contains("Submit", output);
    }
}

public class ListFolderTests
{
    [Fact]
    public void Fold_CollapsesSimilarSiblings()
    {
        var root = new DomNode
        {
            Tag = "ul",
            Children = Enumerable.Range(1, 10).Select(i => new DomNode
            {
                Tag = "li",
                Children = new List<DomNode>
                {
                    new() { Tag = "a", Href = $"https://example.com/{i}", Children = new List<DomNode>
                    {
                        new() { IsText = true, TextContent = $"Item {i}" }
                    }}
                }
            }).Cast<DomNode>().ToList()
        };

        var result = ListFolder.Fold(root, 60, 4);

        // Should have fewer children than original 10
        Assert.True(result.Children.Count < 10);
        // Should contain a folding message
        var texts = result.Children.Where(c => c.IsText).Select(c => c.TextContent).ToList();
        Assert.Contains(texts, t => t.Contains("more similar"));
    }

    [Fact]
    public void Fold_PreservesDissimilarSiblings()
    {
        var root = new DomNode
        {
            Tag = "div",
            Children = new List<DomNode>
            {
                new() { Tag = "h1", Children = new List<DomNode> { new() { IsText = true, TextContent = "Title" } } },
                new() { Tag = "p", Children = new List<DomNode> { new() { IsText = true, TextContent = "Paragraph" } } },
                new() { Tag = "form", Children = new List<DomNode> { new() { Tag = "input", InputType = "text" } } }
            }
        };

        var result = ListFolder.Fold(root, 60, 4);

        // Should not fold — all siblings are different
        Assert.Equal(3, result.Children.Count);
    }
}

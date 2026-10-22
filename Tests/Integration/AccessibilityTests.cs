using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Tests.Integration;

/// <summary>
/// The content element's accessibility surface: default textbox semantics, the
/// AriaLabel/AriaLabelledBy/AriaDescribedBy parameters, aria-placeholder,
/// aria-readonly (created and toggled), and ContentAttributes precedence.
/// </summary>
public class AccessibilityTests : HarnessTestBase
{
    public AccessibilityTests(HarnessFixture fx) : base(fx) { }

    protected override string Route => "harness/accessibility";

    protected override string ReadySelector =>
        "#editor-a11y-attrs[data-lexical-editor='true']";

    [Fact] // no new parameters → textbox defaults and nothing else
    public async Task Default_IsMultilineTextbox_WithNoNamingAttributes()
    {
        var page = await OpenAsync();
        var content = page.Locator("#editor-a11y-default");

        await Expect(content).ToHaveAttributeAsync("role", "textbox");
        await Expect(content).ToHaveAttributeAsync("aria-multiline", "true");
        foreach (var name in new[]
                 {
                     "aria-label", "aria-labelledby", "aria-describedby", "aria-placeholder",
                     "aria-readonly", "aria-expanded", "aria-activedescendant",
                 })
        {
            Assert.Null(await content.GetAttributeAsync(name));
        }
    }

    [Fact] // the issue's "done when" case, checked through the accessibility tree too
    public async Task AriaLabel_AndPlaceholder_NameTheTextbox()
    {
        var page = await OpenAsync();
        var content = page.Locator("#editor-a11y-named");

        await Expect(content).ToHaveAttributeAsync("aria-label", "New comment");
        await Expect(content).ToHaveAttributeAsync("aria-placeholder", "Enter comment...");
        await Expect(page.GetByRole(AriaRole.Textbox, new() { Name = "New comment", Exact = true }))
            .ToHaveIdAsync("editor-a11y-named");
    }

    [Fact] // aria-labelledby/-describedby point at visible text
    public async Task LabelledBy_AndDescribedBy_ResolveToVisibleText()
    {
        var page = await OpenAsync();
        var content = page.Locator("#editor-a11y-labelled");

        await Expect(content).ToHaveAttributeAsync("aria-labelledby", "a11y-label");
        await Expect(content).ToHaveAttributeAsync("aria-describedby", "a11y-help");
        await Expect(content).ToHaveAccessibleNameAsync("Description");
        await Expect(content).ToHaveAccessibleDescriptionAsync("Describe the change.");
    }

    [Fact] // aria-readonly follows contenteditable as ReadOnly toggles at runtime
    public async Task ReadOnly_Toggle_KeepsAriaReadOnlyInSync()
    {
        var page = await OpenAsync();
        var content = page.Locator("#editor-a11y-readonly");

        await Expect(content).ToHaveAttributeAsync("contenteditable", "false");
        await Expect(content).ToHaveAttributeAsync("aria-readonly", "true");

        await page.ClickAsync("#btn-a11y-toggle");
        await Expect(content).ToHaveAttributeAsync("contenteditable", "true");
        await Expect(content).Not.ToHaveAttributeAsync("aria-readonly", "true");
        Assert.Null(await content.GetAttributeAsync("aria-readonly"));

        await page.ClickAsync("#btn-a11y-toggle");
        await Expect(content).ToHaveAttributeAsync("contenteditable", "false");
        await Expect(content).ToHaveAttributeAsync("aria-readonly", "true");
    }

    [Fact] // read-only set from JS (no Blazor render) still updates aria-readonly
    public async Task SetEditable_FromJs_KeepsAriaReadOnlyInSync()
    {
        var page = await OpenAsync();
        var content = page.Locator("#editor-a11y-default");

        await page.EvaluateAsync(
            "async () => (await import('/_content/Blazor.Lexical/blazor-lexical.mjs'))" +
            ".setEditable('editor-a11y-default', false)");
        await Expect(content).ToHaveAttributeAsync("contenteditable", "false");
        await Expect(content).ToHaveAttributeAsync("aria-readonly", "true");
    }

    [Fact] // host attributes land; explicit parameters and structural attributes win
    public async Task ContentAttributes_Splatted_ExplicitParametersAndStructureWin()
    {
        var page = await OpenAsync();
        var content = page.Locator("[data-testid='comment-box']");

        await Expect(content).ToHaveIdAsync("editor-a11y-attrs");
        await Expect(content).ToHaveAttributeAsync("aria-required", "true");
        await Expect(content).ToHaveAttributeAsync("aria-describedby", "from-attributes");
        // AriaLabel beats the dictionary's aria-label.
        await Expect(content).ToHaveAttributeAsync("aria-label", "Explicit label");
        // Structural and JS-owned keys are ignored.
        await Expect(content).ToHaveClassAsync(new Regex("blazor-lexical__content"));
        await Expect(content).Not.ToHaveClassAsync(new Regex("ignored-class"));
        await Expect(content).ToHaveAttributeAsync("contenteditable", "true");
        await Expect(content).ToHaveAttributeAsync("data-lexical-content", "");
        Assert.Null(await content.GetAttributeAsync("aria-readonly"));
        await Expect(page.Locator("#ignored-id")).ToHaveCountAsync(0);
        // Textbox defaults still apply.
        await Expect(content).ToHaveAttributeAsync("role", "textbox");
        await Expect(content).ToHaveAttributeAsync("aria-multiline", "true");
    }
}

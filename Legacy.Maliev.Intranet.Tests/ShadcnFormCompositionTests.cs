using Bunit;
using Legacy.Maliev.Intranet.Client.Shared.Components;
using Maliev.ShadcnBlazor.Components.Forms;
using Microsoft.AspNetCore.Components;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class ShadcnFormCompositionTests : BunitContext
{
    [Fact]
    public void FormFieldAssociatesLabelDescriptionErrorAndControl()
    {
        var cut = Render<ShadcnFormField>(parameters => parameters
            .Add(component => component.Id, "employee-email")
            .Add(component => component.Label, "Email")
            .Add(component => component.Description, "Use your work email")
            .Add(component => component.Error, "Email is invalid")
            .Add(component => component.Required, true)
            .Add(component => component.Control, builder =>
            {
                builder.OpenComponent<ShadcnInput<string>>(0);
                builder.AddAttribute(1, "id", "employee-email");
                builder.CloseComponent();
            }));

        Assert.Equal("employee-email", cut.Find("label").GetAttribute("for"));
        Assert.Equal("employee-email-description employee-email-error", cut.Find("input").GetAttribute("aria-describedby"));
        Assert.Equal("true", cut.Find("input").GetAttribute("aria-invalid"));
        Assert.Equal("Use your work email", cut.Find("#employee-email-description").TextContent);
        Assert.Equal("Email is invalid", cut.Find("#employee-email-error").TextContent);
        Assert.NotNull(cut.Find("[data-required='true']"));
    }

    [Fact]
    public void FormActionsRenderOneSubmitAndInvokeCancelOnce()
    {
        var cancelCount = 0;
        var cut = Render<ShadcnFormActions>(parameters => parameters
            .Add(component => component.SubmitText, "Save")
            .Add(component => component.CancelText, "Cancel")
            .Add(
                component => component.OnCancel,
                EventCallback.Factory.Create<Microsoft.AspNetCore.Components.Web.MouseEventArgs>(
                    this,
                    _ => cancelCount++)));

        var buttons = cut.FindAll("button");
        Assert.Equal(2, buttons.Count);
        Assert.Equal("submit", buttons.Single(button => button.TextContent.Contains("Save", StringComparison.Ordinal)).GetAttribute("type"));
        buttons.Single(button => button.TextContent.Contains("Cancel", StringComparison.Ordinal)).Click();
        Assert.Equal(1, cancelCount);
    }

    [Fact]
    public void BusyFormActionsDisableSubmitAndExposeBusyText()
    {
        var cut = Render<ShadcnFormActions>(parameters => parameters
            .Add(component => component.SubmitText, "Save")
            .Add(component => component.CancelText, "Cancel")
            .Add(component => component.BusyText, "Saving")
            .Add(component => component.IsBusy, true));

        var submit = cut.Find("button[type='submit']");
        Assert.True(submit.HasAttribute("disabled"));
        Assert.Contains("Saving", submit.TextContent, StringComparison.Ordinal);
    }
}

using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace Lodestone.Web.Validation;

/// <summary>
/// A consent checkbox that must be ticked.
/// </summary>
/// <remarks>
/// <para>
/// Replaces <c>[Range(typeof(bool), "true", "true")]</c>, which is unusable on a checkbox once
/// jQuery unobtrusive validation is actually loaded. That attribute emits
/// <c>data-val-range-min="True"</c>, and the client range rule then compares the checkbox's string
/// value "true" against the string "True". The comparison fails for a ticked box, so the browser
/// blocks the submit and the request never reaches the server.
/// </para>
/// <para>
/// This attribute emits the <c>required</c> client rule instead. jQuery validation evaluates
/// <c>required</c> on a checkbox as "must be checked", which is exactly the intended rule, and the
/// same condition is enforced server-side by <see cref="IsValid(object?)"/>.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
public sealed class MustBeAcceptedAttribute : ValidationAttribute, IClientModelValidator
{
    public override bool IsValid(object? value) => value is true;

    public void AddValidation(ClientModelValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var message = FormatErrorMessage(context.ModelMetadata.GetDisplayName());
        context.Attributes.TryAdd("data-val", "true");
        context.Attributes.TryAdd("data-val-required", message);
    }
}

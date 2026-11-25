using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace Dino2.Desktop;

/// <summary>
/// Validación de formato numérico mientras se escribe. El campo inválido se resalta (estilo en Styles.xaml) y
/// explica el problema en su ayuda emergente. Solo comprueba que el texto pueda leerse como número; los rangos
/// físicos se siguen validando al simular, con el mensaje del modelo.
/// </summary>
public static class FieldValidation
{
    public static readonly DependencyProperty IsInvalidProperty = DependencyProperty.RegisterAttached(
        "IsInvalid", typeof(bool), typeof(FieldValidation), new PropertyMetadata(false));

    public static readonly DependencyProperty MessageProperty = DependencyProperty.RegisterAttached(
        "Message", typeof(string), typeof(FieldValidation), new PropertyMetadata(null));

    public static bool GetIsInvalid(DependencyObject element) => (bool)element.GetValue(IsInvalidProperty);
    public static void SetIsInvalid(DependencyObject element, bool value) => element.SetValue(IsInvalidProperty, value);
    public static string? GetMessage(DependencyObject element) => (string?)element.GetValue(MessageProperty);
    public static void SetMessage(DependencyObject element, string? value) => element.SetValue(MessageProperty, value);

    internal const string NotANumber = "No es un número. Usá coma o punto decimal, sin separadores de miles.";
    internal const string NotAnInteger = "Se requiere un número entero.";

    /// <summary>Criterio de las ventanas 4T y Otto: punto o coma decimal, cultura invariante.</summary>
    internal static string? Invariant(string text, bool integer = false)
    {
        if (!double.TryParse(text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double value) ||
            !double.IsFinite(value)) return NotANumber;
        return integer && (value != Math.Truncate(value) || value < int.MinValue || value > int.MaxValue) ? NotAnInteger : null;
    }

    /// <summary>Criterio de los editores 2T: cultura actual, invariante o coma convertida.</summary>
    internal static string? Flexible(string text)
    {
        try { TwoStrokeWindow.Parse(text, ""); return null; }
        catch (ArgumentException) { return NotANumber; }
    }

    /// <summary>Criterio del editor multicilíndrico 4T: cultura actual o invariante.</summary>
    internal static string? CurrentOrInvariant(string text, bool integer = false)
    {
        if ((!double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out double value) &&
             !double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)) || !double.IsFinite(value))
            return NotANumber;
        return integer && (value != Math.Truncate(value) || value < int.MinValue || value > int.MaxValue) ? NotAnInteger : null;
    }

    /// <summary>
    /// Revalida el campo al cambiar su texto o su habilitación. Un campo deshabilitado no participa del cálculo
    /// y nunca se marca.
    /// </summary>
    internal static void Attach(TextBox box, Func<string, string?> problem)
    {
        void Check()
        {
            string? message = box.IsEnabled ? problem(box.Text) : null;
            SetIsInvalid(box, message is not null);
            SetMessage(box, message);
        }
        box.TextChanged += (_, _) => Check();
        box.IsEnabledChanged += (_, _) => Check();
        Check();
    }

    /// <summary>Campos marcados como inválidos dentro de <paramref name="root"/>.</summary>
    internal static IEnumerable<TextBox> Invalid(DependencyObject root) =>
        LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>()
            .SelectMany(child => (child is TextBox box && GetIsInvalid(box) ? [box] : Enumerable.Empty<TextBox>()).Concat(Invalid(child)));
}

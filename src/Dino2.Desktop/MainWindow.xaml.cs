using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Dino2.Core;
using Dino2.Storage;
using Microsoft.Win32;

namespace Dino2.Desktop;

public partial class MainWindow : Window
{
    private readonly List<TextBox> inputs = [];
    private bool populating;
    private bool dirty;
    internal OttoResult? Result { get; private set; }

    public MainWindow()
    {
        InitializeComponent();
        string[] labels = ["Diámetro [mm]", "Carrera [mm]", "Relación de compresión [–]",
            "Presión inicial absoluta [kPa]", "Temperatura inicial [K]", "Calor aportado [kJ/kg aire]",
            "Gamma = cp/cv [–]", "R del gas [J/(kg·K)]", "RPM inicial", "RPM final", "Paso RPM"];
        for (int i = 0; i < labels.Length; i++)
        {
            var label = new Label { Content = labels[i], Padding = new Thickness(0, 8, 0, 3) };
            var input = new TextBox { Name = "Input" + i };
            RegisterName(input.Name, input);
            System.Windows.Automation.AutomationProperties.SetName(input, labels[i]);
            label.Target = input;
            inputs.Add(input);
            Fields.Children.Add(label);
            Fields.Children.Add(input);
            input.TextChanged += (_, _) =>
            {
                if (populating) return;
                dirty = true;
                Title = "Dino2 · configuración modificada *";
                ClearResults();
                Status.Text = "Configuración modificada: vuelva a calcular. Cambios sin guardar.";
            };
        }
        Populate(OttoConfiguration.Example);
        Closing += (_, e) => { if (!ConfirmDiscard()) e.Cancel = true; };
    }

    private void Populate(OttoConfiguration c)
    {
        double[] values = [c.BoreM * 1000, c.StrokeM * 1000, c.CompressionRatio,
            c.InitialPressurePa / 1000, c.InitialTemperatureK, c.HeatInputJPerKg / 1000,
            c.Gamma, c.GasConstantJPerKgK, c.RpmStart, c.RpmEnd, c.RpmStep];
        populating = true;
        for (int i = 0; i < inputs.Count; i++) inputs[i].Text = values[i].ToString("R", CultureInfo.InvariantCulture);
        populating = false;
        dirty = false;
        ClearResults();
    }

    private OttoConfiguration ReadConfiguration()
    {
        double Read(int i)
        {
            if (!double.TryParse(inputs[i].Text.Trim().Replace(',', '.'), NumberStyles.Float,
                CultureInfo.InvariantCulture, out var value) || !double.IsFinite(value))
            {
                inputs[i].Focus();
                throw new ArgumentException($"Revise el campo {i + 1}: introduzca un número finito sin separadores de miles.");
            }
            return value;
        }
        int Rpm(int i)
        {
            double value = Read(i);
            if (value != Math.Truncate(value) || value < 1 || value > 30_000)
                throw new ArgumentException("Las RPM y el paso deben ser enteros entre 1 y 30000.");
            return (int)value;
        }
        var config = new OttoConfiguration(Read(0) / 1000, Read(1) / 1000, Read(2),
            Read(3) * 1000, Read(4), Read(5) * 1000, Read(6), Read(7), Rpm(8), Rpm(9), Rpm(10));
        config.Validate();
        return config;
    }

    private void ClearResults()
    {
        Result = null;
        ResultsTable.ItemsSource = null;
        StatesTable.ItemsSource = null;
        PowerPlot.SetPoints(null);
        TorquePlot.SetPoints(null);
        Summary.Text = "Sin resultados para la configuración actual.";
        Balance.Text = "Calcule para inspeccionar el ciclo.";
        ExportButton.IsEnabled = false;
    }

    private void Calculate_Click(object sender, RoutedEventArgs e) => Guard(() =>
    {
        var result = OttoCycle.Calculate(ReadConfiguration());
        Result = result;
        PowerPlot.SetPoints(result.Points);
        TorquePlot.SetPoints(result.Points);
        ResultsTable.ItemsSource = result.Points;
        StatesTable.ItemsSource = result.States;
        Summary.Text = FormattableString.Invariant($"{result.DisplacementM3 * 1e6:F2} cm³  ·  η ideal {result.Efficiency:P2}  ·  Trabajo {result.WorkJ:F3} J/ciclo  ·  IMEP {result.ImepPa / 1e5:F3} bar");
        Balance.Text = FormattableString.Invariant($"Masa encerrada: {result.MassKg:G8} kg\nQ entrada: {result.HeatInJ:F6} J/ciclo\nQ rechazado (magnitud): {result.HeatOutJ:F6} J/ciclo\nTrabajo neto indicado: {result.WorkJ:F6} J/ciclo\nResidual Qentrada − Qrechazado − W: {result.HeatInJ - result.HeatOutJ - result.WorkJ:E3} J\nRendimiento límite q→0: {result.Efficiency:P4} (con q=0, W/Q no está definido).");
        ExportButton.IsEnabled = true;
        Status.Text = $"{result.Points.Count} regímenes calculados · {OttoCycle.ModelId}. Pase el cursor por las curvas para inspeccionar valores.";
    });

    internal void SaveTo(string path)
    {
        ConfigurationFile.Save(path, ReadConfiguration());
        dirty = false;
        Title = "Dino2 · " + Path.GetFileName(path);
        Status.Text = "Configuración guardada: " + path;
    }

    internal void LoadFrom(string path)
    {
        var config = ConfigurationFile.Load(path); // Validate completely before replacing the editor.
        Populate(config);
        Title = "Dino2 · " + Path.GetFileName(path);
        Status.Text = "Configuración recuperada. Pulse Calcular para regenerar los resultados.";
    }

    private bool ConfirmDiscard() => !dirty || MessageBox.Show(this,
        "Hay cambios de configuración sin guardar. ¿Desea descartarlos?", "Dino2",
        MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    private void Example_Click(object sender, RoutedEventArgs e)
    {
        if (!ConfirmDiscard()) return;
        Populate(OttoConfiguration.Example);
        Title = "Dino2 · ejemplo ilustrativo";
        Status.Text = "Ejemplo ilustrativo cargado; no representa un motor medido.";
    }

    private void Save_Click(object sender, RoutedEventArgs e) => Guard(() =>
    {
        var dialog = new SaveFileDialog { Filter = "Configuración Dino2 (*.json)|*.json", FileName = "motor.json" };
        if (dialog.ShowDialog(this) == true) SaveTo(dialog.FileName);
    });

    private void Open_Click(object sender, RoutedEventArgs e) => Guard(() =>
    {
        if (!ConfirmDiscard()) return;
        var dialog = new OpenFileDialog { Filter = "Configuración Dino2 (*.json)|*.json" };
        if (dialog.ShowDialog(this) == true) LoadFrom(dialog.FileName);
    });

    private void Export_Click(object sender, RoutedEventArgs e) => Guard(() =>
    {
        if (Result is null) return;
        var dialog = new SaveFileDialog { Filter = "Resultados CSV (*.csv)|*.csv", FileName = "otto-ideal.csv" };
        if (dialog.ShowDialog(this) != true) return;
        ResultCsv.Save(dialog.FileName, Result);
        Status.Text = "Resultados e inputs del cálculo exportados: " + dialog.FileName;
    });

    private void Guard(Action action)
    {
        try { action(); }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or JsonException)
        {
            Status.Text = "No se completó la operación: " + ex.Message;
        }
    }
}

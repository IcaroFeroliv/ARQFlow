using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Windows;
using Autodesk.Revit.DB;

namespace ARQFlow.Modules.Modelagem.Views
{
    public partial class RecortePisoView : Window
    {
        public RevitLinkInstance LinkSelecionado { get; private set; }
        public double Folga { get; private set; }
        public double Tolerancia { get; private set; }

        private static readonly string CaminhoConfig = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PluginARQ", "RecortePisoConfig.json");

        private class Config
        {
            public string LinkNome { get; set; } = "";
            public double Folga { get; set; } = 2.0;
            public double Tolerancia { get; set; } = 1.0;
            public double VolumeMin { get; set; } = 0.001;
        }

        public RecortePisoView(List<RevitLinkInstance> links)
        {
            InitializeComponent();
            ComboLinks.ItemsSource = links;
            ComboLinks.DisplayMemberPath = "Name";
            ComboLinks.SelectedIndex = 0;

            CarregarConfig(links);
        }

        private void CarregarConfig(List<RevitLinkInstance> links)
        {
            try
            {
                if (!File.Exists(CaminhoConfig)) return;

                string json = File.ReadAllText(CaminhoConfig);
                var cfg = JsonSerializer.Deserialize<Config>(json);
                if (cfg == null) return;

                TxtFolga.Text = cfg.Folga.ToString("0.##");
                TxtTolerancia.Text = cfg.Tolerancia.ToString("0.##");
                TxtVolumeMin.Text = cfg.VolumeMin.ToString("0.###");

                if (!string.IsNullOrEmpty(cfg.LinkNome))
                {
                    for (int i = 0; i < links.Count; i++)
                    {
                        if (links[i].Name == cfg.LinkNome)
                        {
                            ComboLinks.SelectedIndex = i;
                            break;
                        }
                    }
                }
            }
            catch { /* falha silenciosa — usa valores padrao */ }
        }

        private void SalvarConfig()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(CaminhoConfig));

                var cfg = new Config
                {
                    LinkNome = (ComboLinks.SelectedItem as RevitLinkInstance)?.Name ?? "",
                    Folga = Folga,
                    Tolerancia = Tolerancia,
                    VolumeMin = double.TryParse(TxtVolumeMin.Text, out double vm) ? vm : 0.001
                };

                string json = JsonSerializer.Serialize(cfg, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(CaminhoConfig, json);
            }
            catch { /* falha silenciosa ao salvar */ }
        }

        private void BtnExecutar_Click(object sender, RoutedEventArgs e)
        {
            LinkSelecionado = ComboLinks.SelectedItem as RevitLinkInstance;

            if (!double.TryParse(TxtFolga.Text.Replace(",", "."),
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out double folga))
            {
                MessageBox.Show("Valor de Folga inválido.", "Erro", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!double.TryParse(TxtTolerancia.Text.Replace(",", "."),
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out double tolerancia))
            {
                MessageBox.Show("Valor de Tolerância inválido.", "Erro", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            Folga = folga;
            Tolerancia = tolerancia;

            SalvarConfig();

            this.DialogResult = true;
            this.Close();
        }
    }
}
using Autodesk.Revit.DB;
using ARQFlow.Modules.Modelagem.Commands;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using static ARQFlow.Modules.Modelagem.Commands.RecorteParedeCommand;

namespace ARQFlow.Modules.Modelagem.Views
{
    public partial class RecorteParedeView : Window
    {
        // ── Documento host (passado pelo Command) ────────────────────────────────
        private readonly Document _doc;

        // ── Resultado que o Command vai consumir ─────────────────────────────────
        public RecorteParedeParams Parametros { get; private set; }

        // ── Modelo para preencher o ComboBox ─────────────────────────────────────
        private class LinkItem
        {
            public string Name { get; set; }
            public ElementId Id { get; set; }
        }

        private readonly ElementId _preSelectedLinkId;

        public RecorteParedeView(Document doc, ElementId preSelectedLinkId = null)
        {
            _doc = doc;
            _preSelectedLinkId = preSelectedLinkId;
            InitializeComponent();
            CarregarLinks();
        }

        // ═════════════════════════════════════════════════════════════════════════
        // CARREGAMENTO DOS VÍNCULOS
        // ═════════════════════════════════════════════════════════════════════════

        private void CarregarLinks()
        {
            var links = new FilteredElementCollector(_doc)
                .OfClass(typeof(RevitLinkInstance))
                .Cast<RevitLinkInstance>()
                .Select(l => new LinkItem { Name = l.Name, Id = l.Id })
                .OrderBy(l => l.Name)
                .ToList();

            CmbLinks.ItemsSource = links;

            // Pré-seleciona o link que o usuário clicou na seleção
            if (_preSelectedLinkId != null)
            {
                var match = links.FirstOrDefault(l => l.Id == _preSelectedLinkId);
                if (match != null)
                {
                    CmbLinks.SelectedItem = match;
                    return;
                }
            }

            if (links.Any())
                CmbLinks.SelectedIndex = 0;
        }

        // ═════════════════════════════════════════════════════════════════════════
        // EVENTOS DA UI
        // ═════════════════════════════════════════════════════════════════════════

        private void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            CarregarLinks();
        }

        private void SliderTolerancia_ValueChanged(object sender,
            System.Windows.RoutedPropertyChangedEventArgs<double> e)
        {
            // Atualiza o label ao lado do slider em tempo real
            if (RunTolerancia != null)
                RunTolerancia.Text = $"{(int)SliderTolerancia.Value} cm";
        }

        private void BtnCancelar_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void BtnExecutar_Click(object sender, RoutedEventArgs e)
        {
            // ── Validação ────────────────────────────────────────────────────────
            if (CmbLinks.SelectedItem == null)
            {
                MessageBox.Show(
                    "Selecione um vínculo antes de executar.",
                    "Atenção",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            var categorias = new HashSet<CategoriaRecorte>();
            if (ChkEstrutura.IsChecked == true) categorias.Add(CategoriaRecorte.Estrutura);
            if (ChkDutos.IsChecked == true) categorias.Add(CategoriaRecorte.Dutos);
            if (ChkTubulacoes.IsChecked == true) categorias.Add(CategoriaRecorte.Tubulacoes);
            if (ChkEletrico.IsChecked == true) categorias.Add(CategoriaRecorte.Eletrico);

            if (!categorias.Any())
            {
                MessageBox.Show(
                    "Selecione pelo menos uma categoria.",
                    "Atenção",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            // ── Monta o objeto de parâmetros ─────────────────────────────────────
            var linkSelecionado = (LinkItem)CmbLinks.SelectedItem;
            Parametros = new RecorteParedeParams
            {
                LinkInstanceId = linkSelecionado.Id,
                Categorias = categorias,
                ToleranciaCm = SliderTolerancia.Value
            };

            DialogResult = true;
            Close();
        }
    }
}
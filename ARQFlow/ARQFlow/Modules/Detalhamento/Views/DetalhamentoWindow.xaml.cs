using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Autodesk.Revit.DB;

namespace ARQFlow.Modules.Detalhamento.Views
{
    // Classe estática que guardará as configurações durante a sessão do Revit
    public static class ConfiguracoesMemoria
    {
        public static bool PegarTodos { get; set; } = false;
        public static int TipoSufixo { get; set; } = 0; // 0 = Alfabético, 1 = Numérico, 2 = Direção
        public static bool Lado0 { get; set; } = true;
        public static bool Lado1 { get; set; } = true;
        public static bool Lado2 { get; set; } = true;
        public static bool Lado3 { get; set; } = true;
        public static ElementId ModeloVistaId { get; set; } = null;
    }

    public partial class DetalhamentoWindow : Window
    {
        public bool PegarTodosAmbientes => RbTodos.IsChecked == true;
        public bool SufixoAlfabetico => RbAlfabetico.IsChecked == true;
        public bool SufixoDirecao => RbDirecao.IsChecked == true;
        public ElementId ModeloVistaSelecionadoId => CmbViewTemplates.SelectedValue as ElementId;

        public bool CriarLado0 => ChkLado0.IsChecked == true;
        public bool CriarLado1 => ChkLado1.IsChecked == true;
        public bool CriarLado2 => ChkLado2.IsChecked == true;
        public bool CriarLado3 => ChkLado3.IsChecked == true;

        public DetalhamentoWindow(List<View> viewTemplates)
        {
            InitializeComponent();

            // Popula os templates de vista
            CmbViewTemplates.ItemsSource = viewTemplates;

            // CARREGA AS CONFIGURAÇÕES DA MEMÓRIA
            RbTodos.IsChecked = ConfiguracoesMemoria.PegarTodos;
            RbSelecionar.IsChecked = !ConfiguracoesMemoria.PegarTodos;

            if (ConfiguracoesMemoria.TipoSufixo == 0) RbAlfabetico.IsChecked = true;
            else if (ConfiguracoesMemoria.TipoSufixo == 1) RbNumerico.IsChecked = true;
            else RbDirecao.IsChecked = true;

            ChkLado0.IsChecked = ConfiguracoesMemoria.Lado0;
            ChkLado1.IsChecked = ConfiguracoesMemoria.Lado1;
            ChkLado2.IsChecked = ConfiguracoesMemoria.Lado2;
            ChkLado3.IsChecked = ConfiguracoesMemoria.Lado3;

            // Tenta restaurar o template de vista escolhido anteriormente
            if (ConfiguracoesMemoria.ModeloVistaId != null && viewTemplates.Any(v => v.Id == ConfiguracoesMemoria.ModeloVistaId))
            {
                CmbViewTemplates.SelectedValue = ConfiguracoesMemoria.ModeloVistaId;
            }
            else if (viewTemplates.Count > 0)
            {
                CmbViewTemplates.SelectedIndex = 0;
            }
        }

        private void BtnAvancar_Click(object sender, RoutedEventArgs e)
        {
            // SALVA AS CONFIGURAÇÕES NA MEMÓRIA ANTES DE FECHAR
            ConfiguracoesMemoria.PegarTodos = PegarTodosAmbientes;

            if (SufixoAlfabetico) ConfiguracoesMemoria.TipoSufixo = 0;
            else if (SufixoDirecao) ConfiguracoesMemoria.TipoSufixo = 2;
            else ConfiguracoesMemoria.TipoSufixo = 1;

            ConfiguracoesMemoria.Lado0 = CriarLado0;
            ConfiguracoesMemoria.Lado1 = CriarLado1;
            ConfiguracoesMemoria.Lado2 = CriarLado2;
            ConfiguracoesMemoria.Lado3 = CriarLado3;
            ConfiguracoesMemoria.ModeloVistaId = ModeloVistaSelecionadoId;

            this.DialogResult = true;
            this.Close();
        }

        private void BtnCancelar_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }
    }
}
# ARQ Flow - Revit 2025 Add-in

O **ARQ Flow** é um plugin desenvolvido para otimizar e automatizar fluxos de trabalho de arquitetura dentro do Autodesk Revit 2025. 

Este projeto utiliza **C# e .NET 8** e foi estruturado com foco em escalabilidade, manutenção e trabalho em equipe, utilizando uma **Arquitetura Orientada a Funcionalidades (Feature-Based)** aliada ao padrão **MVVM**.

## 🏗️ Arquitetura do Projeto

Para evitar que o projeto se torne um monólito complexo, a solução foi dividida pelo domínio do negócio (Fases do Projeto Arquitetônico). O código está organizado nas seguintes camadas principais:

* **App:** Ponto de entrada do plugin (`IExternalApplication`). É onde a Ribbon nativa do Revit é construída de forma isolada.
* **Core:** O "motor" do sistema. Contém extensões da API do Revit, validadores e lógicas utilitárias compartilhadas.
* **UI.Shared:** Identidade visual centralizada (estilos XAML genéricos e barras de progresso).
* **Modules:** Onde o desenvolvimento diário acontece. Os comandos são isolados em fluxos de trabalho independentes:
    * `Workflow.Modelagem` (Paredes, Pisos, Recortes)
    * `Workflow.Documentacao` (Tags, Cotas automáticas)
    * `Workflow.Detalhamento` (Vistas, Elevações)

*Nota: Os ícones da Ribbon não ficam soltos em pastas físicas. Eles são embutidos diretamente na `.dll` principal configurando o **Build Action** das imagens como **Resource**.*

## ⚙️ Pré-requisitos

* Autodesk Revit 2025
* Visual Studio 2022 ou superior (Recomendado: Visual Studio 2026)
* SDK do .NET 8.0

## 🚀 Instalação e Compilação (Post-Build Event)

Para garantir um ambiente de desenvolvimento limpo e evitar conflitos com DLLs desnecessárias, este projeto utiliza um script de **Post-Build Event** no Visual Studio. 

Toda vez que você compilar o projeto (Build/Rebuild), o script copiará automaticamente apenas os arquivos estritamente necessários para a pasta `%appdata%` do Revit.

### Como configurar:
1. Clique com o botão direito no projeto `ARQFlow` no **Solution Explorer** e vá em **Properties** (Propriedades).
2. Navegue até **Build > Events** (Eventos de Compilação).
3. No campo **Post-build event**, cole exatamente o script abaixo:

```cmd
:: 1. Cria a pasta dedicada para o plugin se ela ainda não existir
if not exist "$(AppData)\Autodesk\Revit\Addins\2025\ARQFlow" mkdir "$(AppData)\Autodesk\Revit\Addins\2025\ARQFlow"

:: 2. Copia APENAS o arquivo .addin da raiz do projeto para a pasta do Revit
copy "$(ProjectDir)ARQFlow.addin" "$(AppData)\Autodesk\Revit\Addins\2025\" /Y

:: 3. Copia APENAS a .dll principal do seu plugin (ARQFlow.dll)
copy "$(TargetDir)$(TargetFileName)" "$(AppData)\Autodesk\Revit\Addins\2025\ARQFlow\" /Y

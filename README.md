# WebTrayHost

**WebTrayHost** é um hospedeiro de aplicações web de alto desempenho para o sistema operacional Windows, construído sobre **.NET** e **Microsoft WebView2**.

O projeto foi projetado para resolver o problema de serviços web e mensageiros que demandam execução contínua em segundo plano, isolamento completo de credenciais/sessão e suporte nativo à **Bandeja do Sistema (System Tray)** com emissão de notificações na **Central de Ações do Windows (Action Center)**.

---

## 🎯 Problemas Resolvidos

1. **Isolamento Estanque de Sessão**: Cada instância do aplicativo aponta para um diretório exclusivo de dados de usuário (`UserDataFolder`), permitindo executar duas ou mais contas simultâneas do mesmo serviço no mesmo computador sem colisão de cookies ou tokens.
2. **Ciclo de Vida em Segundo Plano (System Tray)**: Permite fechar ou minimizar a janela visual diretamente para a bandeja do sistema sem encerrar os sockets de rede (**WebSockets / SSE / SignalR**).
3. **Bridge Nativa de Notificações com Eliminação de Duplicatas**: Injeta um script na página web que intercepta as chamadas da **Web Notifications API** (`window.Notification`), suprime a notificação padrão do navegador e despacha um alerta nativo do Windows com o logotipo oficial do serviço, som e restauração automática da janela ao clicar no alerta.
4. **Favicon Dinâmico**: Resolve automaticamente o ícone oficial da página web em execução e atualiza o ícone da janela e da bandeja (**System Tray**) sem necessidade de embutir arquivos `.ico` proprietários no repositório.

---

## 🏗️ Arquitetura da Solução

* **Host Runtime**: .NET 10 (C# 14 / Windows Forms com suporte a DPI avançado).
* **Browser Engine**: Microsoft Edge WebView2 Runtime (Evergreen).
* **Subsistema de Notificações**: `Microsoft.Toolkit.Uwp.Notifications` com suporte a Windows Action Center e fallback para `Shell_NotifyIcon` (BalloonTip).
* **Configuração em Camadas**: Carregamento hierárquico via `Microsoft.Extensions.Configuration` (Linha de Comando > Variáveis de Ambiente `WEBTRAY_` > `appsettings.json` > `appsettings.sample.json`).

---

## ⚙️ Configuração

A aplicação pode ser configurada via arquivo JSON, variáveis de ambiente ou argumentos de linha de comando.

### 1. Arquivo de Configuração (`appsettings.json`)

Copie o arquivo `appsettings.sample.json` para `appsettings.json` e ajuste os parâmetros:

```json
{
  "Window": {
    "Title": "Meu Aplicativo de Mensagens",
    "Width": 1280,
    "Height": 850,
    "StartMinimized": false,
    "StartHiddenInTray": false,
    "CloseToTray": true,
    "MinimizeToTray": true
  },
  "Web": {
    "Url": "https://messaging.example.com",
    "UserDataFolder": "%LOCALAPPDATA%\\WebTrayHost\\Perfis\\ContaSecundaria",
    "UserAgent": ""
  },
  "Notifications": {
    "EnableCustomToastNotifications": true,
    "SuppressBrowserDefaultNotifications": true,
    "MonitorDocumentTitle": true,
    "TitleNotificationRegex": "^\\((\\d+)\\)",
    "PlaySound": true
  }
}
```

### 2. Dicionário de Propriedades

| Seção | Propriedade | Tipo | Descrição |
| :--- | :--- | :--- | :--- |
| **Window** | `Title` | `string` | Título da janela e nome exibido no ícone da bandeja. |
| **Window** | `Width` / `Height` | `int` | Dimensões iniciais da janela em pixels. |
| **Window** | `StartMinimized` | `bool` | Inicia a aplicação com a janela minimizada na barra de tarefas. |
| **Window** | `StartHiddenInTray` | `bool` | Inicia o aplicativo diretamente oculto na bandeja do sistema. |
| **Window** | `CloseToTray` | `bool` | Se `true`, clicar no botão `X` oculta a janela em vez de encerrar. |
| **Window** | `MinimizeToTray` | `bool` | Se `true`, minimizar a janela a remove da barra de tarefas e envia para o tray. |
| **Web** | `Url` | `string` | URL do serviço web ou mensageiro a ser hospedado. |
| **Web** | `UserDataFolder` | `string` | Caminho do perfil de cookies/cache. Suporta variáveis do Windows como `%LOCALAPPDATA%`. |
| **Web** | `UserAgent` | `string` | Substitui o cabeçalho User-Agent (opcional). |
| **Notifications** | `EnableCustomToastNotifications` | `bool` | Ativa o disparo de banners nativos do Windows pelo aplicativo C#. |
| **Notifications** | `SuppressBrowserDefaultNotifications` | `bool` | Suprime o alerta padrão do Chromium para evitar notificações duplicadas na tela. |
| **Notifications** | `MonitorDocumentTitle` | `bool` | Monitora contadores de mensagens não lidas no título da página. |
| **Notifications** | `TitleNotificationRegex` | `string` | Expressão regular para extrair contadores numéricos de mensagens (padrão: `^\\((\\d+)\\)`). |
| **Notifications** | `PlaySound` | `bool` | Emite o alerta sonoro padrão do sistema ao exibir o Toast. |

---

### 3. Sobrescrita por Variáveis de Ambiente

Qualquer parâmetro pode ser sobrescrito definindo variáveis de ambiente com o prefixo `WEBTRAY_` e separador duplo de sublinhado `__`:

```powershell
# Exemplo no PowerShell
$env:WEBTRAY_Web__Url = "https://web.chat-corporativo.com"
$env:WEBTRAY_Window__Title = "Chat Secundário"
$env:WEBTRAY_Notifications__EnableCustomToastNotifications = "false"
```

---

## 🚀 Como Compilar e Executar

### Pré-requisitos
* **SDK do .NET 10** instalado.
* **Microsoft Edge WebView2 Runtime** (já incluído no Windows 10 e 11).

### Compilação em Desenvolvimento
```bash
dotnet build
dotnet run
```

### Publicação para Produção (Executável Único)
Para gerar um executável otimizado e autocontido:

```powershell
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o ./publish
```

O binário resultante estará disponível na pasta `./publish/WebTrayHost.exe`.

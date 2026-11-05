# WebTrayHost

**WebTrayHost** é um hospedeiro de aplicações web de alto desempenho para o sistema operacional Windows, construído sobre **.NET** e **Microsoft WebView2**.

O projeto foi projetado para resolver o problema de serviços web e mensageiros que demandam execução contínua em segundo plano, isolamento completo de credenciais/sessão e suporte nativo à **Bandeja do Sistema (System Tray)** com emissão de notificações na **Central de Ações do Windows (Action Center)**.

---

## 🎯 Problemas Resolvidos

1. **Isolamento Estanque de Sessão**: Cada instância do aplicativo aponta para um diretório exclusivo de dados de usuário (`UserDataFolder`), permitindo executar duas ou mais contas simultâneas do mesmo serviço no mesmo computador sem colisão de cookies ou tokens.
2. **Ciclo de Vida em Segundo Plano (System Tray)**: Permite fechar ou minimizar a janela visual diretamente para a bandeja do sistema sem encerrar os sockets de rede (**WebSockets / SSE / SignalR**).
3. **Bridge Nativa de Notificações com Vínculo AUMID**: Intercepta eventos de notificação web e despacha Toasts oficiais do Windows com o logotipo oficial do serviço, som e restauração automática da janela ao clicar no alerta.
4. **Favicon Dinâmico**: Resolve automaticamente o ícone oficial da página web em execução e atualiza o ícone da janela e da bandeja (**System Tray**) sem necessidade de embutir arquivos `.ico` proprietários no repositório.
5. **Ícone Customizado e Invalidação Atômica de Cache**: Suporte opcional a ícones personalizados via `Window:IconPath` (aceitando `.ico` e `.png` com conversão em tempo de execução). Ao alterar o ícone, o host expurga automaticamente o cache do registro do Windows (`HKCU\...\Notifications\Settings\<AUMID>`) e notifica a Shell do Windows (`SHChangeNotify`) para refletir o novo ícone imediatamente nas notificações, sem exigir intervenção manual no registro.

---

## 🏗️ Arquitetura da Solução

* **Host Runtime**: .NET 10 (C# 14 / Windows Forms com suporte a DPI avançado).
* **Browser Engine**: Microsoft Edge WebView2 Runtime (Evergreen).
* **Subsistema de Notificações**: `Microsoft.Toolkit.Uwp.Notifications` com registro formal de AUMID no Windows Shell (`IPropertyStore` / `IShellLinkW`) e invalidação atômica de cache via Win32 Shell API (`SHChangeNotify` / `SHCNE_ASSOCCHANGED`).
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
    "IconPath": "",
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
    "MonitorDocumentTitle": true,
    "TitleNotificationRegex": "^\\((\\d+)\\)",
    "PlaySound": true
  }
}
```

### 2. Dicionário de Propriedades

| Seção | Propriedade | Tipo | Descrição |
| :--- | :--- | :--- | :--- |
| **Window** | `Title` | `string` | Título da janela e nome exibido no ícone da bandeja e no cabeçalho do Toast. |
| **Window** | `IconPath` | `string` | Caminho opcional para arquivo `.ico` ou `.png` personalizado. Se omitido, extrai automaticamente o favicon da página. Suporta variáveis de ambiente (ex: `%LOCALAPPDATA%`) e caminhos relativos ao executável. |
| **Window** | `Width` / `Height` | `int` | Dimensões iniciais da janela em pixels. |
| **Window** | `StartMinimized` | `bool` | Inicia a aplicação com a janela minimizada na barra de tarefas. |
| **Window** | `StartHiddenInTray` | `bool` | Inicia o aplicativo diretamente oculto na bandeja do sistema. |
| **Window** | `CloseToTray` | `bool` | Se `true`, clicar no botão `X` oculta a janela em vez de encerrar. |
| **Window** | `MinimizeToTray` | `bool` | Se `true`, minimizar a janela a remove da barra de tarefas e envia para o tray. |
| **Web** | `Url` | `string` | URL do serviço web ou mensageiro a ser hospedado. |
| **Web** | `UserDataFolder` | `string` | Caminho do perfil de cookies/cache. Suporta variáveis do Windows como `%LOCALAPPDATA%`. |
| **Web** | `UserAgent` | `string` | Substitui o cabeçalho User-Agent (opcional). |
| **Notifications** | `EnableCustomToastNotifications` | `bool` | Ativa o disparo de banners nativos do Windows pelo aplicativo C#. |
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
$env:WEBTRAY_Notifications__EnableCustomToastNotifications = "true"
```

---

## 💡 Exemplo Prático: Execução de Múltiplas Contas do Microsoft Teams

O aplicativo oficial do **Microsoft Teams** para Windows impõe restrições para alternar ou manter duas contas simultâneas (especialmente duas contas pessoais da Microsoft ou contas corporativas distintas sem desconectar). 

Com o **WebTrayHost**, é possível executar uma segunda conta de forma contínua, isolada e com notificações nativas integradas ao **Windows Action Center**.

### 1. Configuração do `appsettings.json` para o Teams

Crie ou ajuste o seu arquivo `appsettings.json` na raiz da aplicação (ou junto ao executável compilado):

```json
{
  "Window": {
    "Title": "Microsoft Teams",
    "IconPath": "",
    "Width": 1280,
    "Height": 850,
    "StartMinimized": false,
    "StartHiddenInTray": false,
    "CloseToTray": true,
    "MinimizeToTray": true
  },
  "Web": {
    "Url": "https://teams.live.com/v2/",
    "UserDataFolder": "%LOCALAPPDATA%\\WebTrayHost\\Perfis\\ContaTeams1",
    "UserAgent": ""
  },
  "Notifications": {
    "EnableCustomToastNotifications": true,
    "MonitorDocumentTitle": true,
    "TitleNotificationRegex": "^\\((\\d+)\\)",
    "PlaySound": true
  }
}
```

> [!NOTE]
> * **Contas Pessoais (MSA)**: Utilize a URL `https://teams.live.com/v2/`.
> * **Contas Corporativas / Escolares (Work/School M365)**: Utilize a URL `https://teams.microsoft.com/v2/`.

### 2. Fluxo Operacional: Como o Host Gerencia o Teams

1. **Isolamento de Sessão e Credenciais**:
   * O WebView2 inicializa apontando para a pasta `%LOCALAPPDATA%\WebTrayHost\Perfis\ContaTeams1`.
   * Todos os tokens de autenticação, cookies e dados de cache do Teams ficam armazenados exclusivamente nesse diretório, garantindo **zero colisão** com o Teams nativo do Windows ou com seus navegadores padrão (Edge, Chrome, Firefox).

2. **Identidade Visual Dinâmica e Criação de Atalho**:
   * Durante o primeiro carregamento, o `WebTrayHost` captura o logotipo oficial do Microsoft Teams através do evento `FaviconChanged`.
   * O ícone é renderizado em alta resolução (96x96 ARGB) e persistido localmente em `cached_favicon.png` e `cached_favicon.ico`.
   * O host registra automaticamente um atalho no Menu Iniciar do Windows com o **AUMID** `WebTrayHost.MicrosoftTeams`. Esse atalho vincula a aplicação ao sistema operacional para que os Toasts exibam a marca e o nome corretos.

3. **Ciclo de Vida em Segundo Plano com Anti-Throttling**:
   * Ao clicar no botão fechar (`X`) ou minimizar, a janela gráfica é ocultada e o aplicativo é recolhido para a bandeja do sistema (**System Tray**).
   * O WebView2 é instanciado com os argumentos `--disable-background-timer-throttling`, `--disable-backgrounding-occluded-windows` e `--disable-renderer-backgrounding`.
   * Isso impede que o Chromium congele o ciclo de execução JavaScript (V8 thread), mantendo as conexões **WebSocket / Long Polling** do Teams ativas em tempo real mesmo com a janela 100% invisível.

4. **Captura e Disparo de Notificações**:
   * Quando uma nova mensagem ou menção é recebida, o Teams invoca a **Web Notifications API** ou altera o título da página (ex: `(1) Microsoft Teams`).
   * O host C# intercepta a requisição via bridge bidirecional (`CoreWebView2.NotificationReceived` e `postMessage`), suprime qualquer modal ineficaz do navegador e despacha um **Toast nativo do Windows Action Center**.
   * A notificação chega com o cabeçalho "Microsoft Teams", o ícone oficial do Teams, o corpo da mensagem e reproduz o som do Windows.

5. **Interação e Restauração Instantânea**:
   * Ao clicar no banner de notificação ou efetuar duplo clique no ícone da bandeja, o evento `ToastNotificationManagerCompat.OnActivated` restaura a janela, trazendo a conversa diretamente para o primeiro plano.

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

Para gerar um executável otimizado via CLI do .NET:

```powershell
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o ./publish
```

O binário resultante estará disponível na pasta `./publish/WebTrayHost.exe`.

---

## 📦 Automação de Releases (`build-release.ps1`)

O repositório inclui o script automatizado `build-release.ps1` para geração padronizada de entregáveis com sanitização de configurações e cálculo de integridade:

```powershell
# Compila automaticamente ambas as variantes (Framework-Dependent e Self-Contained)
# e infere a versão diretamente da tag Git ativa:
.\build-release.ps1

# Compila versão específica explicitamente:
.\build-release.ps1 -Version "1.1.0"

# Compila apenas a variante 100% autocontida (~50MB):
.\build-release.ps1 -SelfContainedOnly
```

Os pacotes `.zip` e seus manifestos criptográficos `.sha256` são gerados na pasta `./dist`.

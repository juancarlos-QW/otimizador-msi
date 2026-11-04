# Otimizador MSI

App para Windows que ajusta a instância principal do MSI App Player usada no Free Fire, buscando um FPS mais estável.

## O que muda

| Ajuste | Novo valor |
|---|---|
| Núcleos | 4 ou 6, à escolha |
| RAM | 4 GB |
| Limite de FPS | 120 |
| Prioridade do `HD-Player.exe` | Alta (vale para todas as instâncias) |

O app não altera o Free Fire nem os arquivos do emulador em `Program Files`. O botão **Restaurar original** volta os valores anteriores.

## Como usar

1. Baixe o `OtimizadorMSI.exe` na página de Releases.
2. Feche o emulador por completo.
3. Abra o app, aceite o pedido de administrador e clique em **Otimizar**.
4. Abra o emulador e confira os valores em **Configurações > Desempenho**.

## Como compilar

```powershell
& 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe' /nologo /target:winexe /platform:x64 /codepage:65001 /optimize /win32manifest:src\app.manifest /out:OtimizadorMSI.exe /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll /r:System.Core.dll src\OtimizadorMSI.cs
```

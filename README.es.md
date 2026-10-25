<p align="right">
  <a href="./README.md"><img src="https://img.shields.io/badge/English-1F2937?style=for-the-badge&labelColor=0B1020" alt="English" /></a>
  <img src="https://img.shields.io/badge/Espa%C3%B1ol-3B82F6?style=for-the-badge&labelColor=0B1020" alt="Español" />
</p>

<div align="center">

<img src="./src/AIBar.Desktop/Assets/aibar.svg" alt="Icono de AIBar: un cerebro lleno de oro líquido" width="128" />

# AIBar

</div>

<div align="center">

[![CI](https://img.shields.io/github/actions/workflow/status/MarsSall/aibar/ci.yml?branch=main&style=for-the-badge&labelColor=0B1020&label=CI)](https://github.com/MarsSall/aibar/actions/workflows/ci.yml)
[![Licencia](https://img.shields.io/github/license/MarsSall/aibar?style=for-the-badge&labelColor=0B1020&color=3B82F6&label=licencia)](./LICENSE)
[![Stars](https://img.shields.io/github/stars/MarsSall/aibar?style=for-the-badge&labelColor=0B1020&color=3B82F6)](https://github.com/MarsSall/aibar/stargazers)
[![Issues](https://img.shields.io/github/issues/MarsSall/aibar?style=for-the-badge&labelColor=0B1020&color=3B82F6)](https://github.com/MarsSall/aibar/issues)
[![Último commit](https://img.shields.io/github/last-commit/MarsSall/aibar?style=for-the-badge&labelColor=0B1020&color=6B7699&label=%C3%BAltimo%20commit)](https://github.com/MarsSall/aibar/commits/main)

</div>

<img src="./.github/assets/banner.svg" alt="Banner de AIBar" width="100%" />

Tus cuotas de IA de un vistazo: uso, ritmo, reinicios y costo estimado de tus suscripciones de Codex y Claude, en la bandeja de Windows.

## Qué es

AIBar es una app chica y local para la bandeja de Windows, pensada para quienes trabajan todo el día con ChatGPT Codex y Claude Code. Muestra cuánto usaste de tus límites de 5 horas y semanales, a qué ritmo los estás consumiendo y cuándo se reinician, sin abrir la página de uso de cada proveedor.

Todo corre en tu máquina. AIBar lee en modo solo lectura los logins oficiales que ya tenés en las CLI, nunca renueva ni reescribe tokens y guarda el historial en archivos SQLite locales.

> **Estado: beta privada.** La versión actual es `0.1.0-beta.4`, un ZIP para Windows x64, autocontenido y sin firmar. Todavía no hay instalador, actualizador ni release público. Ver [distribución de la beta privada](./docs/private-beta.md) (en inglés).

## Stack

<div align="center">
<a href="https://github.com/Gentleman-Programming/gentle-ai">
  <img width="220" src="https://raw.githubusercontent.com/Gentleman-Programming/gentle-ai/main/docs/assets/brand/built-with-gentle-ai.png" alt="Built with Gentle-AI" />
</a>
</div>

<div align="center">

![C#](https://img.shields.io/badge/C%23-512BD4?style=for-the-badge&logo=dotnet&logoColor=white)
![.NET 8](https://img.shields.io/badge/.NET_8-512BD4?style=for-the-badge&logo=dotnet&logoColor=white)
![WPF](https://img.shields.io/badge/WPF-0078D4?style=for-the-badge&logo=windows&logoColor=white)

![SQLite](https://img.shields.io/badge/SQLite-003B57?style=for-the-badge&logo=sqlite&logoColor=white)
![xUnit](https://img.shields.io/badge/xUnit-5C2D91?style=for-the-badge&logo=dotnet&logoColor=white)
![PowerShell](https://img.shields.io/badge/PowerShell-5391FE?style=for-the-badge&logo=powershell&logoColor=white)
![Python](https://img.shields.io/badge/Python-3776AB?style=for-the-badge&logo=python&logoColor=white)
![GitHub Actions](https://img.shields.io/badge/GitHub_Actions-2088FF?style=for-the-badge&logo=githubactions&logoColor=white)

</div>

## Inicio rápido

AIBar está hecho para **Windows x64**. Necesitás el [.NET SDK 8.0.408](https://dotnet.microsoft.com/download/dotnet/8.0), fijado en [`global.json`](./global.json).

```powershell
git clone https://github.com/MarsSall/aibar
cd aibar
dotnet build AIBar.sln
dotnet run --project src/AIBar.Desktop
```

AIBar arranca en la bandeja. Activá Codex y Claude desde **Settings… → Providers**: cada proveedor pide su propio consentimiento y queda apagado hasta que lo aceptes. La bandeja solo contiene **Refresh** (cuando está disponible), **Settings…**, **Start with Windows** y **Exit AIBar**.

## Comandos

| Comando | Para qué sirve |
| --- | --- |
| `dotnet build AIBar.sln` | Compilar todos los proyectos |
| `dotnet test AIBar.sln` | Correr la suite de tests de .NET |
| `dotnet run --project src/AIBar.Desktop` | Correr la app desde el código fuente |
| `python -m unittest discover -s tools/claude-remote-usage/tests` | Testear los helpers remotos de Linux |
| `pwsh -File scripts/Publish-Deterministic.ps1 -PrivateBeta ...` | Generar el ZIP de la beta privada ([detalles](./docs/private-beta.md)) |

## Funcionalidades

- **Panel de cuotas.** Ventanas de 5 horas y semanales de Codex y Claude, con cuenta regresiva al reinicio, plan y antigüedad del dato. Se abre desde la bandeja o con un comando global. Ver [detalles del panel](./docs/panel-details.md).
- **Ritmo e historial.** Guarda localmente cada lectura exitosa y proyecta si te vas a quedar sin cuota antes del reinicio. Ver [historial de cuotas](./docs/quota-history.md).
- **Costo y tokens estimados.** Hoy y los últimos 30 días, a partir de los logs locales de Codex CLI y de los helpers remotos opcionales, a precio de lista. Son estimaciones, no facturas. Ver [costo de uso](./docs/usage-cost.md).
- **Alertas.** Avisos de reinicio, de umbral (por defecto 75/90/100%) y de "se agota antes del reinicio", con efectos opcionales de confeti y brillo en el borde. Ver [alertas](./docs/alerts.md).
- **Estado de los proveedores.** Indicadores de incidentes tomados de las páginas de estado oficiales y acceso con un clic a la página de uso de cada proveedor. Ver [estado de proveedores](./docs/provider-status.md).
- **Copiar resumen.** Copia la cuota que se está mostrando al portapapeles como texto plano. Ver [resumen de cuotas](./docs/quota-summary.md).
- **Configuración.** Sigue los temas claro, oscuro y de alto contraste de Windows: opacidad, tinte por proveedor, colores de las barras, umbrales de alerta e intervalo de actualización. Se puede usar entera con teclado. Ver [configuración](./docs/settings.md).
- **Widget flotante.** ¿No tenés YASB? Activá una tira siempre visible con anillos de cuota desde Configuración, o aceptá la oferta al iniciar, que aparece una sola vez. Arrastrala a cualquier borde de la pantalla.
- **Fuente para YASB.** Si detecta YASB sin la fuente, AIBar te ofrece instalar IosevkaTerm Nerd Font, incluida en el ZIP. También podés instalarla desde Configuración, solo para tu usuario, sin permisos de administrador ni descargas.
- **Widget para YASB.** CustomWidget opcional para [YASB](https://github.com/amnweb/yasb) que muestra una instantánea sanitizada de la cuota en tu barra. Ver [`yasb/README.txt`](./yasb/README.txt).
- **Uso remoto.** Helpers opcionales para Linux que mandan por SSH la cuota de Claude y los totales de tokens de Codex desde otra máquina. Ver [`tools/claude-remote-usage`](./tools/claude-remote-usage/README.md).

### Proveedores soportados

| Proveedor | Datos | Conexión |
| --- | --- | --- |
| ChatGPT Codex | Límites de 5 horas y semanales, uso local de tokens | Login existente de Codex CLI (`auth.json`), solo lectura |
| Claude Pro / Max | Límites de 5 horas y semanales, uso de tokens | Login existente de Claude Code, solo lectura, o un helper en otra máquina |

La lectura de cuotas usa endpoints no documentados de los proveedores. Pueden cambiar o dejar de funcionar en cualquier momento.

## Arquitectura

| Ruta | Responsabilidad |
| --- | --- |
| `src/AIBar.Domain` | Políticas puras: ritmo, proyección, alertas, precios, métricas derivadas |
| `src/AIBar.Application` | Proveedores, frontera de credenciales, stores SQLite, escáneres, coordinadores |
| `src/AIBar.Desktop` | Host WPF de la bandeja, panel, configuración, notificaciones, temas |
| `tests/` | Suites de xUnit y harnesses de procesos |
| `tools/` | Supervisor de empaquetado y helpers remotos para Linux |
| `yasb/` | Lector del CustomWidget de YASB, ejemplos y tests de Pester |
| `scripts/` | Publicación determinística de la beta privada |

Las notas de desarrollo y de diseño de tests están en [`docs/dev/`](./docs/dev/README.md).

## Privacidad y datos locales

- Cada proveedor viene **apagado por defecto** y necesita su propio consentimiento explícito. Desactivar un proveedor corta su consulta periódica y borra su cuota en caché.
- Las credenciales se leen solo de los archivos de login de las CLI oficiales. AIBar nunca renueva tokens, nunca modifica un login y no guarda credenciales propias.
- Los datos quedan en `%LOCALAPPDATA%\AIBar`: caché de cuotas, historial, analíticas y la instantánea para YASB. AIBar no tiene backend ni telemetría.
- Las consultas van directo desde tu PC a OpenAI, Anthropic y sus páginas de estado públicas.
- AIBar ya no escanea el uso local de OpenCode ni Pi. Los archivos existentes se conservan hasta **Clear AIBar Data**, cuya operación interna sigue borrando las cachés, el historial y las exportaciones locales. El botón en Configuración llegará en la próxima actualización de ajustes; ya no es una acción de la bandeja.

## Roadmap

La firma de código, un instalador, la actualización automática y un release público están planeados. Hoy no están disponibles.

## Contribuir

Los issues y pull requests son bienvenidos. Leé [CONTRIBUTING.md](./CONTRIBUTING.md) para la configuración, los tests y el flujo de PR, y [SECURITY.md](./SECURITY.md) para reportar vulnerabilidades en privado.

## Licencia

[MIT](./LICENSE) © MarsSall. AIBar no está afiliado a OpenAI ni a Anthropic. Las marcas pertenecen a sus respectivos dueños.

---

<div align="center">

**⭐ Si AIBar te resulta útil, dejale una estrella ⭐**

</div>

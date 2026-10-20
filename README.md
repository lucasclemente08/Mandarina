# Mandarina

Conversor de archivos para Windows 11 que se usa arrastrando: empezás a arrastrar un archivo,
tocás **Mayús** y aparece una rueda con los formatos posibles. Lo soltás sobre uno y listo:
el resultado queda al lado del original. Inspirado en Tangerine (macOS). Todo corre sin conexión.

## Instalar (1 clic)

**[⬇ Descargar Mandarina.exe](https://github.com/lucasclemente08/Mandarina/releases/latest/download/Mandarina.exe)** (Windows 10/11, x64; requiere el [.NET 9 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/9.0) y [ffmpeg](https://www.gyan.dev/ffmpeg/builds/) para audio y video).

Doble clic en `Mandarina.exe` y respondé **Sí**. Se instala solo para tu usuario, sin permisos de administrador, en
`%LOCALAPPDATA%\Programs\Mandarina`. Además:

- agrega **"Convertir con Mandarina"** al clic derecho del Explorador,
- la arranca sola con Windows,
- crea un acceso en el menú Inicio.

Para instalar sin preguntas: `Mandarina.exe --install`. Para quitarla: menú del ícono → **Desinstalar…**,
o `Mandarina.exe --uninstall`.

## Uso: tres formas de abrir la rueda

| Forma | Cómo | Elegís con |
|---|---|---|
| **Arrastrar** | Empezá a arrastrar archivos y tocá **Mayús** (convertir) o **Alt** (editar) | soltando sobre la opción |
| **Atajo** | Seleccioná archivos en el Explorador o el escritorio y apretá **Ctrl+Mayús+M** | un clic |
| **Clic derecho** | Clic derecho → **Convertir con Mandarina** (en Windows 11 aparece en *Mostrar más opciones*) | un clic |

En modo clic, hacer clic en el **centro** de la rueda alterna entre convertir y editar. **Esc**, o un clic
fuera de la rueda, la cierra.

El resultado queda al lado del original, que nunca se modifica. Al terminar aparece una notificación; si
hacés clic, se abre la carpeta con el archivo seleccionado.

## Qué sabe hacer

| Tipo | Convertir (Mayús) | Editar (Alt) |
|---|---|---|
| Imágenes (png, jpg, webp, gif, bmp, tiff, ico, heic, avif) | PNG, JPG, WEBP, GIF, BMP, TIFF, ICO (multi-tamaño), AVIF, PDF (varias → un PDF) | 50 %, máx 1920 px, rotar, espejo, blanco y negro, sin metadatos, comprimir |
| Video (mp4, mkv, mov, avi, webm, …) | MP4, MKV, WEBM, MOV, AVI, GIF, y audio MP3/WAV/M4A/OGG/FLAC/OPUS | comprimir, 720p, 480p, sin audio, rotar, ×2, fotograma PNG, unir |
| Audio (mp3, wav, m4a, ogg, flac, …) | MP3, WAV, M4A, OGG, FLAC, OPUS | comprimir, mono, normalizar, quitar silencios, ×1.5, unir |
| PDF | PNG / JPG (una imagen por página) | unir, separar páginas, rotar, invertir orden, comprimir |
| Word (docx, doc, odt, rtf, txt, html) | PDF, DOCX, ODT, RTF, TXT, HTML | — |
| Excel (xlsx, xls, ods, csv) | PDF, XLSX, CSV, ODS | — |
| PowerPoint (pptx, ppt, odp) | PDF, PPTX, ODP | — |
| Markdown | DOCX, HTML, ODT, RTF (con Pandoc) | — |

## Requisitos

- Windows 10 2004+ / Windows 11 con el **.NET 9 Desktop Runtime**.
- **ffmpeg** en el PATH (o en `tools\ffmpeg.exe` junto al exe) para audio, video, WEBP y AVIF:
  `winget install Gyan.FFmpeg`
- Documentos: **Microsoft Office** (se usa por COM) o, si no está, **LibreOffice**
  (`winget install TheDocumentFoundation.LibreOffice`). Markdown necesita **Pandoc** (`winget install JohnMacFarlane.Pandoc`).
- Las opciones que dependen de una herramienta que no está instalada no aparecen en la rueda.

## Línea de comandos

```
Mandarina.exe --list <archivos>                 # acciones disponibles
Mandarina.exe --run <acción> <archivos>         # ejecuta una (ej. img.to.webp)
Mandarina.exe --preview convert|edit out.png <archivos>   # dibuja la rueda en un PNG
Mandarina.exe --open <archivos>                 # abre la rueda en modo clic (lo usa el menú contextual)
```

Para diagnosticar problemas, definí la variable de entorno `MANDARINA_LOG=C:\temp\mandarina.log` antes de abrirla.

## Compilar

```
dotnet publish src/Mandarina -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o dist
```

## Cómo funciona

- `Interop/DragWatcher.cs`: hooks globales de mouse y teclado (solo observan). Detecta "botón
  apretado + movimiento" y luego la pulsación de Mayús/Alt.
- `Interop/InstancePipe.cs`: el Explorador abre un proceso por cada archivo seleccionado; esos procesos le pasan
  los archivos por un *named pipe* a la instancia que ya corre, y esta los junta en una sola rueda.
- `Interop/ExplorerSelection.cs`: lee la selección de la ventana del Explorador activa (incluida la pestaña activa
  de Windows 11) o del escritorio, para el atajo.
- `Installer.cs`: instalación por usuario (copia, menú contextual, inicio con Windows y acceso directo).
- `UI/WheelWindow.cs`: ventana transparente, siempre visible y sin foco, centrada en el cursor. Es un
  destino de arrastre OLE: recibe la lista de archivos y devuelve "copiar", así el origen nunca borra el original.
- `Actions/`: catálogo de acciones por tipo de archivo. Imágenes con WIC (WPF), audio y video con
  ffmpeg, PDF con el motor de Windows (`Windows.Data.Pdf`) y PDFsharp, y documentos con Office por COM, LibreOffice o Pandoc.

## Limitaciones conocidas

- No ve arrastres desde aplicaciones que corren como administrador (restricción de Windows/UIPI).
- "Unir" video necesita clips con la misma resolución y con pista de audio.
- En Windows 11 la entrada del clic derecho aparece en "Mostrar más opciones" (Mayús+F10). Para que salga en
  el menú corto, la app tendría que estar empaquetada y firmada (MSIX).
- PDF → Word no está incluido: Word se cuelga al hacerlo por automatización.

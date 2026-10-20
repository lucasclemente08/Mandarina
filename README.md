# Mandarina

Conversor de archivos para Windows 11 que se usa arrastrando: empezás a arrastrar un archivo,
tocás **Mayús** y aparece una rueda con los formatos posibles. Lo soltás sobre uno y listo:
el resultado queda al lado del original. Inspirado en Tangerine (macOS). Todo corre sin conexión.

## Uso

1. Ejecutá `dist\Mandarina.exe`. Aparece una mandarina en el área de notificación.
2. Arrastrá uno o varios archivos (desde el Explorador, el escritorio, etc.).
3. Sin soltar el botón:
   - **Mayús** → rueda de **conversión** (PNG, JPG, MP4, MP3, PDF…)
   - **Alt** → rueda de **edición** (rotar, comprimir, 720p, unir PDFs…)
4. Soltá sobre una opción. Soltar en el centro o apretar **Esc** cancela.

Al terminar aparece una notificación; si hacés clic, se abre la carpeta con el archivo seleccionado.
El original nunca se modifica ni se mueve.

Desde el menú del ícono podés pausarla o hacer que **inicie con Windows**.

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
```

## Compilar

```
dotnet publish src/Mandarina -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o dist
```

## Cómo funciona

- `Interop/DragWatcher.cs`: hooks globales de mouse y teclado (solo observan). Detecta "botón
  apretado + movimiento" y luego la pulsación de Mayús/Alt.
- `UI/WheelWindow.cs`: ventana transparente, siempre visible y sin foco, centrada en el cursor. Es un
  destino de arrastre OLE: recibe la lista de archivos y devuelve "copiar", así el origen nunca borra el original.
- `Actions/`: catálogo de acciones por tipo de archivo. Imágenes con WIC (WPF), audio y video con
  ffmpeg, PDF con el motor de Windows (`Windows.Data.Pdf`) y PDFsharp, y documentos con Office por COM, LibreOffice o Pandoc.

## Limitaciones conocidas

- No ve arrastres desde aplicaciones que corren como administrador (restricción de Windows/UIPI).
- "Unir" video necesita clips con la misma resolución y con pista de audio.
- PDF → Word no está incluido: Word se cuelga al hacerlo por automatización.

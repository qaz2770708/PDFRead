# PDFRead

PDFRead is an early Windows desktop PDF reader. Milestone 1 proves the first document-data loop: open and render a PDF, add a page-area highlight, store it in a separate sidecar file, and restore it after reopening.

## Milestone 1

- Windows Forms desktop executable for Windows 10 and Windows 11.
- Uses the built-in Windows PDF rendering API.
- Adds rectangular page-area highlights in normalized page coordinates.
- Stores PDF-version-specific data beside the source PDF: `report.pdf` pairs with `report.pdfdata`.
- Identifies the PDF version by SHA-256. If content changes, the reader asks whether to create a new version record or explicitly reuse old annotations.
- Source PDFs are opened read-only. Saving annotations never rewrites the source PDF.

## Build and run

Build on Windows with the .NET SDK and .NET Framework 4.8 targeting references:

```powershell
./build.ps1
```

This workspace keeps Git metadata in the ignored `work/pdfread.git` directory because its root `.git` directory is read-only in the desktop sandbox. Use `./git-sync.ps1 status` and `./git-sync.ps1 push -u origin main` for Git operations here.

Run `outputs/PDFReader.exe`, open `samples/demo.pdf`, enable **区域高亮：拖拽框选**, and drag on the page. The app writes `demo.pdfdata` beside the opened PDF.

The current release binary, sample, and validation notes are in `outputs/` and `docs/`.

## Current limits

This milestone is a persistence and coordinate proof. It does not yet include multi-tab windows, text-selection highlights, underline, text comments, handwriting, shapes, bookmarks, sticky notes, or annotated-PDF export.

## Verification

See [docs/M1-validation.md](docs/M1-validation.md) for the render, save/reopen, coordinate round-trip, and source-hash results.

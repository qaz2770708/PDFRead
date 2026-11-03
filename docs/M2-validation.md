# Milestone 2 validation

## Build

- Ran `build.ps1` in Release configuration on Windows.
- Result: success, 0 warnings, 0 errors.
- Confirmed the release output includes `PDFReader.exe`, `PDFReader.exe.config`, and `System.Runtime.WindowsRuntime.dll`.

## Integration probe

Compiled and ran `M2VerificationProbe` against a generated three-page PDF with portrait, landscape, and portrait pages. All checks passed:

- Page rectangles are horizontally centered, including a landscape page among portrait pages.
- Pages are stacked in continuous vertical reading order with the intended 34 px visual gap; the viewer content is taller than its viewport and can scroll.
- Page-relative reading position (page and normalized vertical offset) survives capture and restore.
- Current page, offset, zoom, and annotation data save to `.pdfdata` and reload correctly.
- Changing PDF bytes changes the SHA-256 profile; the same-basename sidecar prompts once, keeps the old record, creates an empty new-version record, and reuses it on the next open.
- A newer complete `.tmp` recovery snapshot can be selected and loaded.
- A valid `.bak` is offered after the main sidecar is corrupted, and a protective recovery copy is retained.
- If both `.tmp` and `.bak` are valid while the main file is unusable, the candidate with the higher revision is selected and preserved.
- A v1 sidecar migrates to v2 with defaults for zoom and page offset.
- Two PDFs open in separate tabs, and reopening the same path selects the existing tab instead of duplicating it.
- The source PDF SHA-256 is unchanged after opening, rendering, and sidecar operations.

The probe also produced `continuous-view.png` and `continuous-seam.png` in the ignored `work/m2-tests/` directory. The captures show centered pages and the next page following the previous page in the same scroll surface. A separate attempt to capture the live application window through desktop automation timed out twice, so this validation does not claim a mouse-driven manual UI check.

## Scope note

These checks validate the reading and persistence foundation only. The current annotation implementation is rectangular area highlighting; other annotation tools, bookmarks, sticky notes, tab tear-off windows, and annotated PDF export remain future work.

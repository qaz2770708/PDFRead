from pathlib import Path
objects={
1:b'<< /Type /Catalog /Pages 2 0 R /Outlines 12 0 R /Names << /Dests 18 0 R >> /Dests << /legacy-first [3 0 R /Fit] >> >>',
2:b'<< /Type /Pages /Kids [3 0 R 4 0 R 5 0 R 6 0 R] /Count 4 >>',
7:b'<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>',
12:b'<< /Type /Outlines /First 13 0 R /Last 14 0 R /Count 5 >>',
13:b'<< /Title (Chapter 1 - PDF page 2) /Parent 12 0 R /Dest [4 0 R /XYZ 0 600 1.2] /Next 14 0 R /First 15 0 R /Last 17 0 R /Count 3 >>',
14:b'<< /Title (Named target - PDF page 4) /Parent 12 0 R /Prev 13 0 R /A << /S /GoTo /D (named-last) >> >>',
15:b'<< /Title (Legacy name - PDF page 1) /Parent 13 0 R /Next 16 0 R /Dest /legacy-first >>',
16:b'<< /Title (Rotated crop - PDF page 3) /Parent 13 0 R /Prev 15 0 R /Next 17 0 R /Dest 20 0 R >>',
17:b'<< /Title (External target - no local page) /Parent 13 0 R /Prev 16 0 R /A << /S /URI /URI (https://example.com) >> >>',
18:b'<< /Kids [19 0 R] >>',
19:b'<< /Names [(named-last) << /D [6 0 R /FitH 700] >>] >>',
20:b'[5 0 R /XYZ 120 300 1.1]',
21:b'<< /Type /Annot /Subtype /Link /Rect [60 660 480 710] /Border [0 0 0] /A << /S /GoTo /D (named-last) >> >>'
}
for i in range(4):
 extra=b' /Annots [21 0 R]' if i==0 else b''
 if i==2:extra+=b' /CropBox [20 30 580 750] /Rotate 90'
 objects[3+i]=b'<< /Type /Page /Parent 2 0 R /MediaBox [0 0 600 800] /Resources << /Font << /F1 7 0 R >> >> /Contents '+str(8+i).encode()+b' 0 R'+extra+b' >>'
 content=('BT /F1 22 Tf 60 740 Td (PDF actual page '+str(i+1)+') Tj 0 -55 Td (Click here to jump to PDF page 4) Tj ET').encode()
 content+=b'\n'+b'\n'.join(('BT /F1 16 Tf 60 '+str(y)+' Td (Page '+str(i+1)+' content at PDF coordinate '+str(y)+') Tj ET').encode() for y in range(620,60,-80))
 objects[8+i]=b'<< /Length '+str(len(content)).encode()+b' >>\nstream\n'+content+b'\nendstream'
raw=bytearray(b'%PDF-1.7\n');offsets=[0]
for i in range(1,22):
 offsets.append(len(raw));raw+=str(i).encode()+b' 0 obj\n'+objects[i]+b'\nendobj\n'
xref=len(raw);raw+=b'xref\n0 22\n0000000000 65535 f \n'
for off in offsets[1:]:raw+=('%010d 00000 n \n'%off).encode()
raw+=b'trailer\n<< /Size 22 /Root 1 0 R >>\nstartxref\n'+str(xref).encode()+b'\n%%EOF\n'
Path('samples/navigation.pdf').write_bytes(raw)

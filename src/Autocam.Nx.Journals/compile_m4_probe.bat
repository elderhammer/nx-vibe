@echo off
cd /d C:\Users\21505\Code\nx-vibe\src\Autocam.Nx.Journals
"C:\Windows\Microsoft.NET\Framework64\v4.0.30319\vbc.exe" /target:exe /out:"C:\nx-vibe-journal-out\M4_GeoProbe.exe" /r:System.dll /r:System.Core.dll /r:"C:\Program Files\Siemens\NX2406\NXBIN\managed\NXOpen.dll" /r:"C:\Program Files\Siemens\NX2406\NXBIN\managed\NXOpen.Utilities.dll" M4_GeoProbe.vb

$env:PATH = 'C:\Git\ProjectF\tmp\documents\word_soffice;' + $env:PATH
$docx = Get-ChildItem -LiteralPath 'C:\Git\ProjectF\output\documents' -Filter '*.docx' | Select-Object -First 1 -ExpandProperty FullName
$qa = 'C:\Git\ProjectF\tmp\documents\qa4'
New-Item -ItemType Directory -Force -Path $qa | Out-Null
& 'C:\Users\dk601\.cache\codex-runtimes\codex-primary-runtime\dependencies\python\python.exe' `
  'C:\Users\dk601\.cache\codex-runtimes\codex-primary-runtime\plugins\openai-primary-runtime\plugins\documents\skills\documents\render_docx.py' `
  $docx --output_dir $qa --emit_pdf --verbose
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Get-ChildItem -LiteralPath $qa | Select-Object Name, Length

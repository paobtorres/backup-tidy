#requires -Version 5.1
<#
.SYNOPSIS
  Organiza backups de celulares SIN BORRAR NADA.

.DESCRIPTION
  1. Detecta duplicados exactos (mismo contenido, hash MD5) y copias .HEIC.jpg
     convertidas de un HEIC que ya existe.
  2. Clasifica en Fotos / Videos / Capturas / LivePhotos / WhatsApp / Guardadas.
  3. Agrupa por Anio\Anio-Mes usando la fecha EXIF o de metadatos del video.
     (La fecha de modificacion del archivo NO sirve: es la fecha del restore.)
  4. Documentos (PDF, Office, comprimidos, otros) van a Documentos\<tipo>\, y sus
     duplicados a Documentos\_duplicados\.
  5. Descartes (notas de voz, contactos, .aae, stickers, favicons...) quedan en su
     lugar, salvo que se ejecute con -PapeleraDescartes: ahi van a la Papelera.

  Por defecto SOLO ANALIZA: genera reportes\plan_<fecha>.csv y un resumen.
  No mueve ni modifica ningun archivo.

  Con -Ejecutar mueve los archivos segun el plan. En el mismo disco mover es
  instantaneo y no usa espacio extra. Cada movimiento queda registrado en
  reportes\movimientos_<fecha>.csv para poder deshacerlo con -Deshacer.

  Nunca borra ni sobrescribe. Los duplicados van a _duplicados\ (con la misma
  estructura de carpetas original) para que los revises y borres a mano.

.EXAMPLE
  .\organizar_backup.ps1
  Analiza y genera el plan. No toca nada.

.EXAMPLE
  .\organizar_backup.ps1 -Ejecutar -Plan .\reportes\plan_20260929_1200.csv
  Ejecuta un plan ya revisado (no vuelve a calcular hashes).

.EXAMPLE
  .\organizar_backup.ps1 -CompararCon D:\05_archive\backup_celulares_organizado
  Analiza un backup nuevo: lo que ya este organizado va a _duplicados.

.EXAMPLE
  .\organizar_backup.ps1 -Ejecutar -Plan .\reportes\plan_20261012_1200.csv -PapeleraDescartes
  Ejecuta el plan y manda los descartes a la Papelera de reciclaje.

.EXAMPLE
  .\organizar_backup.ps1 -Deshacer .\reportes\movimientos_20260929_1300.csv
  Devuelve cada archivo a su ubicacion original (lo enviado a la Papelera se
  restaura desde la Papelera).
#>
[CmdletBinding()]
param(
    [string]$Origen   = 'D:\05_archive\backup_celulares',
    [string]$Destino  = 'D:\05_archive\backup_celulares_organizado',
    [string]$Reportes,
    [switch]$Ejecutar,
    [string]$Plan,
    [string]$Deshacer,
    # Carpetas ya organizadas: lo que ya exista ahi se manda a _duplicados en vez de volver a entrar
    [string[]]$CompararCon,
    # Al ejecutar, envia los descartes a la Papelera de reciclaje (nunca borra en forma definitiva)
    [switch]$PapeleraDescartes
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

# En PowerShell 5.1 $PSScriptRoot esta vacio dentro de param(), y desde ISE/VS Code
# (F5/F8) la ruta del script puede no existir: se usa la carpeta actual como respaldo
$carpetaScript = if ($MyInvocation.MyCommand.Path) { Split-Path -Parent $MyInvocation.MyCommand.Path } else { (Get-Location).Path }

# Acepta rutas absolutas o relativas a la carpeta actual o a la carpeta del script
function Resolve-Entrada([string]$ruta) {
    if (-not $ruta) { return $ruta }
    foreach ($cand in $ruta, (Join-Path $carpetaScript $ruta)) {
        if (Test-Path -LiteralPath $cand -PathType Leaf) { return (Resolve-Path -LiteralPath $cand).ProviderPath }
    }
    throw "No se encuentra el archivo: $ruta"
}
$Plan     = Resolve-Entrada $Plan
$Deshacer = Resolve-Entrada $Deshacer

if (-not $Reportes) {
    $Reportes = if ($Plan) { Split-Path -Parent $Plan }
                elseif ($Deshacer) { Split-Path -Parent $Deshacer }
                else { Join-Path $carpetaScript 'reportes' }
}

$sep   = (Get-Culture).TextInfo.ListSeparator   # ';' en Excel en espanol
$marca = Get-Date -Format 'yyyyMMdd_HHmm'
$ci    = [Globalization.CultureInfo]::InvariantCulture
[void][IO.Directory]::CreateDirectory($Reportes)

$extFoto      = @('.jpg', '.jpeg', '.heic', '.heif', '.png', '.dng', '.gif', '.webp', '.tif', '.tiff', '.bmp',
                  '.nef', '.cr2', '.cr3', '.arw', '.orf', '.raf', '.rw2')   # + RAW de camaras
# Notas de voz, audios de apps, contactos, calendario, ediciones de iPhone, stickers animados de WhatsApp
$extDescarte  = @('.opus', '.m4a', '.mp3', '.wav', '.caf', '.amr', '.vcf', '.ics', '.aae', '.favicon', '.was', '.identifier')
$nomDescarte  = @('thumbs.db', 'desktop.ini', '.picasa.ini')
$tiposDoc     = [ordered]@{
    PDF         = @('.pdf')
    Office      = @('.doc', '.docx', '.xls', '.xlsx', '.ppt', '.pptx', '.pbix', '.odt', '.ods', '.odp', '.rtf')
    Comprimidos = @('.zip', '.rar', '.7z', '.gz', '.tar')
}
$extVideo     = @('.mov', '.mp4', '.m4v', '.3gp', '.avi', '.mkv', '.mpg', '.mpeg', '.wmv')
$extExifGdi   = @('.jpg', '.jpeg', '.tif', '.tiff')
$rxMensajes   = '\\(Messages|Message Attachments|Attachments|WhatsApp[^\\]*|Telegram[^\\]*)\\'
$rxCaptura    = '(screenshot|screen shot|captura|screen_recording|rpreplay)'
$rxConvertida = '\.(hei[cf]|png|dng|mov|mp4)\.jpe?g$'   # vistas previas que crea el programa de restore
$rxMiniatura  = '\.thm\.jpe?g$'
$rxApps       = '\\App Photos\\'                         # cache interna de apps (Notas, Duo...)
$rxWhatsApp   = '\\WhatsApp[^\\]*\\'
$rxOrganizado = '\\(Fotos|Videos|LivePhotos)\\\d{4}\\(\d{4}-\d{2})\\([^\\]+)$'
$maxLivePhoto = 30MB
$fechaMin     = Get-Date '2000-01-01'
$fechaMax     = (Get-Date).AddDays(1)

function Test-FechaValida($d) {
    return ($d -is [datetime]) -and $d -ge $fechaMin -and $d -le $fechaMax
}

function Format-Csv([string]$s) { '"' + ($s -replace '"', '""') + '"' }

# Media = foto/video, Descarte = va a la papelera con -PapeleraDescartes, Documento = todo lo demas
function Get-TipoArchivo([IO.FileInfo]$f) {
    $e = $f.Extension.ToLower()
    if ($extFoto -contains $e -or $extVideo -contains $e) { return 'Media' }
    if ($extDescarte -contains $e -or $nomDescarte -contains $f.Name.ToLower()) { return 'Descarte' }
    return 'Documento'
}

function Get-TipoDocumento([string]$ext) {
    foreach ($k in $tiposDoc.Keys) { if ($tiposDoc[$k] -contains $ext) { return $k } }
    return 'Otros'
}

# Lee fecha de captura y marca de camara sin decodificar la imagen completa (rapido)
function Read-ExifGdi([string]$ruta) {
    $r = @{ Fecha = $null; Marca = $null }
    $fs = $null; $img = $null
    try {
        $fs  = [IO.File]::OpenRead($ruta)
        $img = [Drawing.Image]::FromStream($fs, $false, $false)
        $ids = $img.PropertyIdList
        if ($ids -contains 0x010F) {
            $r.Marca = [Text.Encoding]::ASCII.GetString($img.GetPropertyItem(0x010F).Value).Trim([char]0, ' ')
        }
        foreach ($id in 0x9003, 0x9004, 0x0132) {
            if ($ids -contains $id) {
                $txt = [Text.Encoding]::ASCII.GetString($img.GetPropertyItem($id).Value).Trim([char]0, ' ')
                $d = [datetime]::MinValue
                if ([datetime]::TryParseExact($txt, 'yyyy:MM:dd HH:mm:ss', $ci, 'None', [ref]$d)) { $r.Fecha = $d; break }
            }
        }
    } catch {
    } finally {
        if ($img) { $img.Dispose() }
        if ($fs)  { $fs.Dispose() }
    }
    return $r
}

# Propiedades de Windows (HEIC, DNG, PNG, videos). Las fechas vienen en UTC.
$shell = New-Object -ComObject Shell.Application
$carpetasShell = @{}
function Read-PropShell([IO.FileInfo]$f, [string]$prop) {
    try {
        $dir = $f.DirectoryName
        if (-not $carpetasShell.ContainsKey($dir)) { $carpetasShell[$dir] = $shell.Namespace($dir) }
        $item = $carpetasShell[$dir].ParseName($f.Name)
        if ($item) { return $item.ExtendedProperty($prop) }
    } catch { }
    return $null
}

# Fechas en el nombre: IMG-20190315-WA0001, Screenshot_2019-03-15, 20190315_123456...
function Get-FechaNombre([string]$nombre) {
    $m = [regex]::Match($nombre, '(?<!\d)((?:19|20)\d{2})[-_.]?(0[1-9]|1[0-2])[-_.]?(0[1-9]|[12]\d|3[01])(?!\d)')
    if ($m.Success) {
        return New-Object DateTime ([int]$m.Groups[1].Value), ([int]$m.Groups[2].Value), ([int]$m.Groups[3].Value)
    }
    return $null
}

function Get-InfoMedia([IO.FileInfo]$f, [bool]$esVideo, [bool]$mtimeDudosa) {
    $fecha = $null; $fuente = $null; $marcaCam = $null
    $ext = $f.Extension.ToLower()

    if (-not $esVideo -and $extExifGdi -contains $ext) {
        $ex = Read-ExifGdi $f.FullName
        $marcaCam = $ex.Marca
        if (Test-FechaValida $ex.Fecha) { $fecha = $ex.Fecha; $fuente = 'exif' }
    }
    if (-not $fecha) {
        $prop = if ($esVideo) { 'System.Media.DateEncoded' } else { 'System.Photo.DateTaken' }
        $v = Read-PropShell $f $prop
        if ($v -is [datetime]) {
            $d = [datetime]::SpecifyKind($v, 'Utc').ToLocalTime()
            if (Test-FechaValida $d) { $fecha = $d; $fuente = 'metadatos' }
        }
    }
    if ($ext -eq '.png') { $marcaCam = Read-PropShell $f 'System.Photo.CameraManufacturer' }
    if (-not $fecha) {
        $d = Get-FechaNombre $f.Name
        if (Test-FechaValida $d) { $fecha = $d; $fuente = 'nombre' }
    }
    if (-not $fecha) {
        $fecha = $f.LastWriteTime
        $fuente = if ($mtimeDudosa) { 'archivo_dudoso' } else { 'archivo' }
    }
    return @{ Fecha = $fecha; Fuente = $fuente; Marca = "$marcaCam" }
}

function Get-DestinoLibre([string]$ruta, $usados) {
    $dir  = [IO.Path]::GetDirectoryName($ruta)
    $base = [IO.Path]::GetFileNameWithoutExtension($ruta)
    $ext  = [IO.Path]::GetExtension($ruta)
    $cand = $ruta; $k = 2
    while ($usados.Contains($cand) -or (Test-Path -LiteralPath $cand)) {
        $cand = Join-Path $dir ('{0}_{1}{2}' -f $base, $k, $ext); $k++
    }
    [void]$usados.Add($cand)
    return $cand
}

# Clave "backup + nombre sin extension" para emparejar IMG_1234.HEIC con IMG_1234.MOV (Live Photo)
function Get-ClaveLive([IO.FileInfo]$f) {
    $partes = $f.FullName.Substring($Origen.Length).TrimStart('\').Split('\')
    $raiz = ($partes | Select-Object -First ([Math]::Min(2, $partes.Count - 1))) -join '\'
    return ('{0}|{1}' -f $raiz, [IO.Path]::GetFileNameWithoutExtension($f.Name)).ToLower()
}

# IMG_1234.HEIC.jpg / IMG_1234_2.JPG -> img_1234
function Get-NombreBase([string]$nombre) {
    $p = $nombre.IndexOf('.')
    if ($p -gt 0) { $nombre = $nombre.Substring(0, $p) }
    return ($nombre -replace '_\d$', '').ToLower()
}

function Invoke-Analisis {
    Write-Host "1/4 Listando archivos en $Origen ..."
    $todos = New-Object Collections.Generic.List[object]
    $descartes = New-Object Collections.Generic.List[object]
    $esDoc = New-Object 'Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
    foreach ($f in (Get-ChildItem -LiteralPath $Origen -File -Recurse -Force -ErrorAction SilentlyContinue)) {
        switch (Get-TipoArchivo $f) {
            'Media'     { $todos.Add($f) }
            'Documento' { $todos.Add($f); [void]$esDoc.Add($f.FullName) }
            'Descarte'  { $descartes.Add($f) }
        }
    }
    $todos = $todos.ToArray()
    Write-Host "    $($todos.Count - $esDoc.Count) fotos/videos, $($esDoc.Count) documentos, $($descartes.Count) descartes"

    # Archivos ya organizados contra los que se compara (no se mueven nunca)
    $referencia = @()
    foreach ($c in $CompararCon) {
        if (-not (Test-Path -LiteralPath $c)) { Write-Warning "No existe para comparar: $c"; continue }
        Write-Host "    Comparando tambien con $c ..."
        $referencia += @(Get-ChildItem -LiteralPath $c -File -Recurse -Force -ErrorAction SilentlyContinue |
            Where-Object { (Get-TipoArchivo $_) -ne 'Descarte' })
    }
    $esRef = New-Object 'Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
    foreach ($f in $referencia) { [void]$esRef.Add($f.FullName) }
    if ($referencia.Count) { Write-Host "    $($referencia.Count) archivos ya organizados" }

    # Misma foto en otro formato (HEIC vs JPG convertido): mismo nombre y mismo mes
    $yaOrganizado = @{}
    foreach ($f in $referencia) {
        $m = [regex]::Match($f.FullName, $rxOrganizado)
        if ($m.Success) {
            $k = '{0}|{1}' -f $m.Groups[2].Value, (Get-NombreBase $f.Name)
            if (-not $yaOrganizado.ContainsKey($k)) { $yaOrganizado[$k] = New-Object Collections.Generic.List[string] }
            $yaOrganizado[$k].Add($f.FullName)
        }
    }
    $reemplazados = New-Object 'Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)

    Write-Host '2/4 Buscando duplicados exactos (solo se calcula hash de archivos con tamano repetido) ...'
    $porTamano = @{}
    foreach ($f in ($todos + $referencia)) {
        if (-not $porTamano.ContainsKey($f.Length)) { $porTamano[$f.Length] = New-Object Collections.Generic.List[object] }
        $porTamano[$f.Length].Add($f)
    }
    # Solo grupos con al menos un archivo nuevo (no tiene sentido comparar organizados entre si)
    $candidatos = @($porTamano.Values | Where-Object {
            $_.Count -gt 1 -and @($_ | Where-Object { -not $esRef.Contains($_.FullName) }).Count -gt 0
        } | ForEach-Object { $_ })
    $hashes = @{}; $porHash = @{}; $i = 0
    foreach ($f in $candidatos) {
        $i++
        if ($i % 200 -eq 0) { Write-Progress -Activity 'Calculando hash' -Status "$i / $($candidatos.Count)" -PercentComplete (100 * $i / $candidatos.Count) }
        try { $h = (Get-FileHash -LiteralPath $f.FullName -Algorithm MD5).Hash } catch { continue }
        $hashes[$f.FullName] = $h
        if (-not $porHash.ContainsKey($h)) { $porHash[$h] = New-Object Collections.Generic.List[object] }
        $porHash[$h].Add($f)
    }
    Write-Progress -Activity 'Calculando hash' -Completed

    # Se conserva la copia "mas limpia": no convertida, fuera de Mensajes, nombre y ruta mas cortos
    # Si ya existe en lo organizado, se conserva esa copia (preferentemente fuera de _duplicados)
    $esDuplicadoDe = @{}
    foreach ($grupo in $porHash.Values) {
        if ($grupo.Count -lt 2) { continue }
        $refs   = @($grupo | Where-Object { $esRef.Contains($_.FullName) } | Sort-Object @{ e = { if ($_.FullName -match '\\_duplicados') { 1 } else { 0 } } }, FullName)
        $nuevos = @($grupo | Where-Object { -not $esRef.Contains($_.FullName) })
        if ($refs.Count) {
            foreach ($d in $nuevos) { $esDuplicadoDe[$d.FullName] = 'Ya existe en: ' + $refs[0].FullName }
            continue
        }
        $orden = @($nuevos | Sort-Object @{ e = { if ($_.Name -match $rxConvertida) { 1 } else { 0 } } },
                                         @{ e = { if ($_.FullName -match $rxMensajes) { 1 } else { 0 } } },
                                         @{ e = { $_.Name.Length } }, @{ e = { $_.FullName.Length } }, FullName)
        foreach ($d in ($orden | Select-Object -Skip 1)) {
            $esDuplicadoDe[$d.FullName] = 'Igual a: ' + $orden[0].FullName.Substring($Origen.Length).TrimStart('\')
        }
    }

    $media = @($todos | Where-Object { -not $esDoc.Contains($_.FullName) })
    $rutas = New-Object 'Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
    $fotosLive = New-Object 'Collections.Generic.HashSet[string]'
    foreach ($f in $media) {
        [void]$rutas.Add($f.FullName)
        if ($f.Extension.ToLower() -in '.heic', '.heif', '.jpg', '.jpeg') { [void]$fotosLive.Add((Get-ClaveLive $f)) }
    }

    # Carpetas restauradas: casi todos los archivos tienen la fecha del restore, no la real
    $diasPorCarpeta = @{}
    foreach ($f in $media) {
        if (-not $diasPorCarpeta.ContainsKey($f.DirectoryName)) { $diasPorCarpeta[$f.DirectoryName] = @{} }
        $dias = $diasPorCarpeta[$f.DirectoryName]
        $dias[$f.LastWriteTime.Date] = 1 + [int]$dias[$f.LastWriteTime.Date]
    }
    $carpetasMtimeDudosa = New-Object 'Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
    foreach ($kv in $diasPorCarpeta.GetEnumerator()) {
        $cuentas = $kv.Value.Values | Measure-Object -Sum -Maximum
        if ($cuentas.Sum -ge 20 -and $cuentas.Maximum / $cuentas.Sum -ge 0.5) { [void]$carpetasMtimeDudosa.Add($kv.Key) }
    }

    Write-Host '3/4 Leyendo fechas y clasificando (puede tardar) ...'
    $plan  = New-Object Collections.Generic.List[object]
    $usados = New-Object 'Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
    $i = 0
    foreach ($f in $todos) {
        $i++
        if ($i % 200 -eq 0) { Write-Progress -Activity 'Clasificando' -Status "$i / $($todos.Count)" -PercentComplete (100 * $i / $todos.Count) }
        $rel = $f.FullName.Substring($Origen.Length).TrimStart('\')
        $ext = $f.Extension.ToLower()
        $esVideo = $extVideo -contains $ext
        $fecha = ''; $fuente = ''; $nota = ''

        if ($esDoc.Contains($f.FullName)) {
            # Los documentos no tienen fecha fiable (es la de la exportacion): se ordenan por tipo
            $cat = 'Documentos'
            if ($esDuplicadoDe.ContainsKey($f.FullName)) {
                $cat  = 'DocumentosDuplicados'
                $nota = $esDuplicadoDe[$f.FullName]
                $dest = Join-Path $Destino "Documentos\_duplicados\$($f.Name)"
            } else {
                $dest = Join-Path $Destino ('Documentos\{0}\{1}' -f (Get-TipoDocumento $ext), $f.Name)
            }
        } elseif ($esDuplicadoDe.ContainsKey($f.FullName)) {
            $cat  = 'Duplicados'
            $nota = $esDuplicadoDe[$f.FullName]
            $dest = Join-Path $Destino (Join-Path '_duplicados' $rel)
        } elseif ($f.Name -match $rxConvertida -and $rutas.Contains(($f.FullName -replace '\.jpe?g$', ''))) {
            $cat  = 'Duplicados'
            $nota = 'Vista previa JPG de un original que existe al lado'
            $dest = Join-Path $Destino (Join-Path '_duplicados_conversiones' $rel)
        } else {
            $info = Get-InfoMedia $f $esVideo $carpetasMtimeDudosa.Contains($f.DirectoryName)
            $enMensajes = $f.FullName -match $rxMensajes
            if ($f.FullName -match $rxApps) { $cat = 'Apps' }
            elseif ($f.FullName -match $rxWhatsApp) { $cat = 'WhatsApp' }
            elseif ($f.Name -match $rxMiniatura) { $cat = 'Miniaturas' }
            elseif ($esVideo) {
                if ($f.Name -match $rxCaptura) { $cat = 'Capturas' }
                elseif ($enMensajes) { $cat = 'Mensajes' }
                elseif ($f.Length -le $maxLivePhoto -and $fotosLive.Contains((Get-ClaveLive $f))) { $cat = 'LivePhotos' }
                else { $cat = 'Videos' }
            } else {
                if ($f.Name -match $rxCaptura) { $cat = 'Capturas' }
                elseif ($enMensajes) { $cat = 'Mensajes' }
                elseif ($ext -eq '.png' -and -not $info.Marca) { $cat = 'Capturas' }
                elseif ($extExifGdi -contains $ext -and -not $info.Marca) { $cat = 'Guardadas' }  # sin camara: WhatsApp, descargas
                else { $cat = 'Fotos' }
            }
            $carpeta = Join-Path $Destino $cat
            if ($info.Fuente -eq 'archivo_dudoso') { $carpeta = Join-Path $carpeta '_fecha_dudosa' }
            $carpeta = Join-Path $carpeta ('{0:yyyy}\{0:yyyy-MM}' -f $info.Fecha)
            $dest   = Join-Path $carpeta $f.Name

            $claveOrg = '{0:yyyy-MM}|{1}' -f $info.Fecha, (Get-NombreBase $f.Name)
            if ($cat -in 'Fotos', 'Videos', 'LivePhotos' -and $info.Fuente -in 'exif', 'metadatos' -and $yaOrganizado.ContainsKey($claveOrg)) {
                $similares = $yaOrganizado[$claveOrg]
                $jpgs = @($similares | Where-Object { $_ -match '\\Fotos\\.*\.jpe?g$' })
                if ($ext -in '.heic', '.heif' -and $jpgs.Count) {
                    # El HEIC original entra; el JPG convertido sale a revision
                    $nota = 'Reemplaza JPG convertido: ' + ($jpgs -join ' ; ')
                    foreach ($j in $jpgs) {
                        if (-not $reemplazados.Add($j)) { continue }
                        $relJ = $j.Substring($Destino.Length).TrimStart('\')
                        $fj = New-Object IO.FileInfo $j
                        $plan.Add([pscustomobject]@{
                            Categoria = 'JpgReemplazado'; Fecha = ('{0:yyyy-MM-dd HH:mm}' -f $info.Fecha); FechaFuente = ''
                            TamanoMB = [math]::Round($fj.Length / 1MB, 2); Bytes = $fj.Length
                            Origen = $j; Destino = (Get-DestinoLibre (Join-Path $Destino (Join-Path '_jpg_reemplazados' $relJ)) $usados)
                            Hash = ''; Nota = 'Reemplazado por: ' + $f.FullName
                        })
                    }
                } else {
                    $nota = 'Mismo nombre y mes que: ' + $similares[0]
                    $cat  = 'Duplicados'
                    $dest = Join-Path $Destino (Join-Path '_duplicados_probables' $rel)
                }
            }
            $fecha  = '{0:yyyy-MM-dd HH:mm}' -f $info.Fecha
            $fuente = $info.Fuente
        }
        $dest = Get-DestinoLibre $dest $usados
        $plan.Add([pscustomobject]@{
            Categoria   = $cat
            Fecha       = $fecha
            FechaFuente = $fuente
            TamanoMB    = [math]::Round($f.Length / 1MB, 2)
            Bytes       = $f.Length
            Origen      = $f.FullName
            Destino     = $dest
            Hash        = $hashes[$f.FullName]
            Nota        = $nota
        })
    }
    Write-Progress -Activity 'Clasificando' -Completed

    foreach ($f in $descartes) {
        $plan.Add([pscustomobject]@{
            Categoria = 'Descartar'; Fecha = ''; FechaFuente = ''
            TamanoMB = [math]::Round($f.Length / 1MB, 2); Bytes = $f.Length
            Origen = $f.FullName; Destino = ''; Hash = ''
            Nota = 'Va a la Papelera solo si se ejecuta con -PapeleraDescartes'
        })
    }

    $rutaPlan = Join-Path $Reportes "plan_$marca.csv"
    $plan | Export-Csv -LiteralPath $rutaPlan -NoTypeInformation -Encoding UTF8 -Delimiter $sep

    Write-Host '4/4 Resumen'
    $plan | Group-Object Categoria | ForEach-Object {
        [pscustomobject]@{ Categoria = $_.Name; Archivos = $_.Count; GB = [math]::Round(($_.Group | Measure-Object Bytes -Sum).Sum / 1GB, 2) }
    } | Sort-Object GB -Descending | Format-Table -AutoSize | Out-Host
    Write-Host 'Origen de la fecha (archivo_dudoso = fecha del restore, va a _fecha_dudosa):'
    $plan | Where-Object { $_.FechaFuente } | Group-Object FechaFuente |
        Select-Object @{ n = 'Fuente'; e = { $_.Name } }, Count | Format-Table -AutoSize | Out-Host
    Write-Host "Plan guardado en: $rutaPlan"
    return $rutaPlan
}

# Capacidad de la Papelera del disco en bytes, o $null si no se puede confirmar que guarda (no borra) los archivos
function Get-CapacidadPapelera([string]$ruta) {
    try {
        $letra = [IO.Path]::GetPathRoot($ruta).TrimEnd('\')
        $vol = Get-CimInstance Win32_Volume -Filter "DriveLetter='$letra'"
        $guid = $vol.DeviceID -replace '.*(\{[^}]+\}).*', '$1'
        $k = Get-ItemProperty "HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\BitBucket\Volume\$guid" -ErrorAction Stop
        if ($k.NukeOnDelete -eq 1) { return $null }
        return [int64]$k.MaxCapacity * 1MB
    } catch { return $null }
}

# Envia a la Papelera los descartes. Si una carpeta solo contiene descartes (o esta vacia) se envia
# entera, que es mucho mas rapido que archivo por archivo. Nunca toca archivos que no esten en el plan.
function Send-DescartesAPapelera($filas, $log) {
    Add-Type -AssemblyName Microsoft.VisualBasic
    $FS = [Microsoft.VisualBasic.FileIO.FileSystem]
    $desc = New-Object 'Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
    $bytes = 0
    foreach ($r in $filas) { if ([IO.File]::Exists($r.Origen)) { [void]$desc.Add($r.Origen); $bytes += [int64]$r.Bytes } }
    if (-not $desc.Count) { Write-Host 'No hay descartes para enviar a la Papelera.'; return }

    $raiz = $Origen
    if (@($desc | Where-Object { -not $_.StartsWith($raiz + '\', 'OrdinalIgnoreCase') }).Count) {
        Write-Warning "Hay descartes fuera de $raiz; ejecuta con -Origen apuntando a la carpeta del plan."; return
    }
    $cap = Get-CapacidadPapelera $raiz
    if (-not $cap -or $bytes -gt $cap) {
        Write-Warning ('No se envian descartes: la Papelera de {0} no esta confirmada o no alcanza ({1:N1} GB). Quedan en su lugar.' -f [IO.Path]::GetPathRoot($raiz), ($bytes / 1GB))
        return
    }

    $script:enviados = 0
    function Send-Arbol([string]$dir) {
        $archivos = [IO.Directory]::GetFiles($dir, '*', [IO.SearchOption]::AllDirectories)
        if (-not @($archivos | Where-Object { -not $desc.Contains($_) }).Count) {
            $FS::DeleteDirectory($dir, 'OnlyErrorDialogs', 'SendToRecycleBin')
            foreach ($a in $archivos) { $log.WriteLine((@($a, 'PAPELERA', 'Descartar') | ForEach-Object { Format-Csv $_ }) -join $sep) }
            $script:enviados += $archivos.Count
            return
        }
        foreach ($sub in [IO.Directory]::GetDirectories($dir)) { Send-Arbol $sub }
        foreach ($a in [IO.Directory]::GetFiles($dir)) {
            if ($desc.Contains($a)) {
                $FS::DeleteFile($a, 'OnlyErrorDialogs', 'SendToRecycleBin')
                $log.WriteLine((@($a, 'PAPELERA', 'Descartar') | ForEach-Object { Format-Csv $_ }) -join $sep)
                $script:enviados++
            }
        }
    }
    # Se recorre desde las subcarpetas: la carpeta de origen en si nunca se envia a la Papelera
    foreach ($sub in [IO.Directory]::GetDirectories($raiz)) { Send-Arbol $sub }
    foreach ($a in [IO.Directory]::GetFiles($raiz)) {
        if ($desc.Contains($a)) { $FS::DeleteFile($a, 'OnlyErrorDialogs', 'SendToRecycleBin'); $script:enviados++ }
    }
    Write-Host ('Enviados a la Papelera: {0} descartes ({1:N2} GB), junto con las carpetas que quedaron vacias.' -f $script:enviados, ($bytes / 1GB))
}

function Invoke-Plan([string]$rutaPlan) {
    $todas = @(Import-Csv -LiteralPath $rutaPlan -Delimiter $sep -Encoding UTF8)
    $filas = @($todas | Where-Object { $_.Categoria -ne 'Descartar' })
    $descartar = @($todas | Where-Object { $_.Categoria -eq 'Descartar' })
    $rutaLog = Join-Path $Reportes "movimientos_$marca.csv"
    $log = New-Object IO.StreamWriter($rutaLog, $false, (New-Object Text.UTF8Encoding($true)))
    $log.WriteLine((@('Origen', 'Destino', 'Categoria') | ForEach-Object { Format-Csv $_ }) -join $sep)
    $ok = 0; $omitidos = 0; $errores = 0; $i = 0
    try {
        foreach ($r in $filas) {
            $i++
            if ($i % 200 -eq 0) { Write-Progress -Activity 'Moviendo' -Status "$i / $($filas.Count)" -PercentComplete (100 * $i / $filas.Count); $log.Flush() }
            if ([IO.Path]::GetPathRoot($r.Origen) -ne [IO.Path]::GetPathRoot($r.Destino)) {
                Write-Warning "Distinto disco, se omite: $($r.Origen)"; $omitidos++; continue
            }
            if (-not [IO.File]::Exists($r.Origen) -or [IO.File]::Exists($r.Destino)) { $omitidos++; continue }
            if ((New-Object IO.FileInfo $r.Origen).Length -ne [int64]$r.Bytes) { $omitidos++; continue }
            try {
                [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($r.Destino))
                [IO.File]::Move($r.Origen, $r.Destino)
                $log.WriteLine((@($r.Origen, $r.Destino, $r.Categoria) | ForEach-Object { Format-Csv $_ }) -join $sep)
                $ok++
            } catch {
                $errores++; Write-Warning "No se pudo mover $($r.Origen): $($_.Exception.Message)"
            }
        }
        Write-Progress -Activity 'Moviendo' -Completed
        Write-Host "Movidos: $ok   Omitidos: $omitidos   Errores: $errores"
        if ($descartar.Count) {
            if ($PapeleraDescartes) { Send-DescartesAPapelera $descartar $log }
            else { Write-Host "Descartes sin tocar: $($descartar.Count) (usa -PapeleraDescartes para enviarlos a la Papelera)" }
        }
    } finally {
        $log.Dispose()
    }
    Write-Host "Registro para deshacer: $rutaLog"
}

function Invoke-Deshacer([string]$rutaLog) {
    $filas = @(Import-Csv -LiteralPath $rutaLog -Delimiter $sep -Encoding UTF8)
    [array]::Reverse($filas)
    $ok = 0; $omitidos = 0
    foreach ($r in $filas) {
        if ($r.Destino -eq 'PAPELERA') { $omitidos++; continue }   # se restaura desde la Papelera
        if ([IO.File]::Exists($r.Destino) -and -not [IO.File]::Exists($r.Origen)) {
            [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($r.Origen))
            [IO.File]::Move($r.Destino, $r.Origen); $ok++
        } else { $omitidos++ }
    }
    Write-Host "Restaurados: $ok   Omitidos: $omitidos"
}

# ---------------------------------------------------------------------------
if ($Deshacer) { Invoke-Deshacer $Deshacer; return }

if (-not (Test-Path -LiteralPath $Origen)) { throw "No existe la carpeta de origen: $Origen" }
$Origen  = (Resolve-Path -LiteralPath $Origen).ProviderPath.TrimEnd('\')
$Destino = [IO.Path]::GetFullPath($Destino).TrimEnd('\')
if ($Destino.StartsWith($Origen + '\', 'OrdinalIgnoreCase')) { throw 'El destino no puede estar dentro del origen.' }

if ($Ejecutar) {
    if (-not $Plan) { $Plan = Invoke-Analisis }
    Write-Host ''
    Write-Host "Se moveran los archivos segun: $Plan" -ForegroundColor Yellow
    Write-Host 'No se borra nada; se guarda un registro para deshacer.' -ForegroundColor Yellow
    if ($PapeleraDescartes) { Write-Host 'Los descartes (notas de voz, contactos, .aae...) se envian a la Papelera de reciclaje.' -ForegroundColor Yellow }
    if ((Read-Host 'Escribe SI para continuar') -ne 'SI') { Write-Host 'Cancelado.'; return }
    Invoke-Plan $Plan
} else {
    [void](Invoke-Analisis)
    Write-Host ''
    Write-Host 'Modo analisis: no se movio ningun archivo.' -ForegroundColor Green
    Write-Host 'Revisa el plan en Excel y luego ejecuta:  .\organizar_backup.ps1 -Ejecutar -Plan <ruta del plan>'
}

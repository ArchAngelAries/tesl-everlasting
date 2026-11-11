$ErrorActionPreference = 'Stop'
$gameRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$cecil = Join-Path $gameRoot 'BepInEx/core/Mono.Cecil.dll'
Add-Type -Path $cecil
$outDir = Join-Path $PSScriptRoot 'ref'
New-Item -ItemType Directory -Path $outDir -Force | Out-Null
$resolver = [Mono.Cecil.DefaultAssemblyResolver]::new()
$resolver.AddSearchDirectory((Join-Path $gameRoot 'The Elder Scrolls Legends_Data/Managed'))
$parameters = [Mono.Cecil.ReaderParameters]::new()
$parameters.AssemblyResolver = $resolver
$assembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $gameRoot 'The Elder Scrolls Legends_Data/Managed/Assembly-CSharp.dll'), $parameters)
function Publicize-Type($type) {
    if ($type.Name -eq '<Module>' -or $type.FullName.Split('/')[0] -notin @('SPTESL.Simulation', 'SPTESL.ServerCardCatalog', 'SPTESL.PlayerServiceClient')) { return }
    $visibility = if ($type.IsNested) { [Mono.Cecil.TypeAttributes]::NestedPublic } else { [Mono.Cecil.TypeAttributes]::Public }
    $type.Attributes = [Mono.Cecil.TypeAttributes](([int64]$type.Attributes -band (0xFFFFFFFFL -bxor [int64][Mono.Cecil.TypeAttributes]::VisibilityMask)) -bor [int64]$visibility)
    foreach ($field in $type.Fields) {
        $field.Attributes = [Mono.Cecil.FieldAttributes](([int]$field.Attributes -band (0xFFFF -bxor [int][Mono.Cecil.FieldAttributes]::FieldAccessMask)) -bor [int][Mono.Cecil.FieldAttributes]::Public)
    }
    foreach ($method in $type.Methods) {
        if ($method.Name -ne '.cctor') {
            $method.Attributes = [Mono.Cecil.MethodAttributes](([int]$method.Attributes -band (0xFFFF -bxor [int][Mono.Cecil.MethodAttributes]::MemberAccessMask)) -bor [int][Mono.Cecil.MethodAttributes]::Public)
        }
    }
    foreach ($nested in $type.NestedTypes) { Publicize-Type $nested }
}
foreach ($type in $assembly.MainModule.Types) { Publicize-Type $type }
$output = Join-Path $outDir 'Assembly-CSharp.dll'
$assembly.Write($output)
$assembly.Dispose()
Write-Output "Created compile-only reference: $output"

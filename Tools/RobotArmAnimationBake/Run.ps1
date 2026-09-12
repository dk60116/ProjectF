param([switch]$Check)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$destination = Join-Path $repo 'FactorioProject/Assets/Scripts/Map/RobotArmAnimationCurves.cs'
$text = @"
// Generated from the four authored Robot Arm .anim assets. See Tools/RobotArmAnimationBake.
using UnityEngine;
internal static class RobotArmAnimationCurves
{
    internal readonly struct Track
    {
        internal readonly string Path;
        internal readonly AnimationCurve X, Y, Z;
        internal Track(string path, Keyframe[] x, Keyframe[] y, Keyframe[] z)
        { Path = path; X = new AnimationCurve(x); Y = new AnimationCurve(y); Z = new AnimationCurve(z); }
        internal Quaternion Evaluate(float time) => Quaternion.Euler(X.Evaluate(time), Y.Evaluate(time), Z.Evaluate(time));
    }

"@
$text = $text.Replace("`r`n", "`n")
$trackCount = 0
foreach ($type in @('Robot Arm', 'Long Robot arm')) {
    foreach ($clip in @('PickItem', 'DropItem')) {
        $path = Join-Path $repo ("FactorioProject/Assets/Animation/InstallationObject/Machine/$type/$clip.anim")
        $raw = [IO.File]::ReadAllText($path).Replace("`r`n", "`n")
        if ($raw -notmatch '(?m)^    m_StopTime: 1$') { throw "The entity presentation expects one-second authored clips: $path" }
        foreach ($channel in @('Rotation', 'Position', 'Scale')) {
            if ($raw -notmatch ("  m_" + $channel + "Curves: \[\]")) { throw "Unsupported $channel curves in $path; extend the baker before changing the asset." }
        }
        $section = ($raw -split "  m_EulerCurves:`n", 2)[1] -split '  m_PositionCurves:', 2
        if ($section[0] -match 'weightedMode: [1-9]') { throw "Weighted keys require extending the baker: $path" }
        $tracks = [regex]::Matches($section[0], '  - curve:\n([\s\S]*?)    path: (.*)\n')
        if ($tracks.Count -eq 0) { throw "No Euler tracks: $path" }
        $label = $(if ($type -eq 'Robot Arm') { 'Short' } else { 'Long' }) + $(if ($clip -eq 'PickItem') { 'Pick' } else { 'Drop' })
        $text += "    internal static readonly Track[] $label = new Track[]`n    {`n"
        foreach ($track in $tracks) {
            $keys = [regex]::Matches($track.Groups[1].Value, 'time: ([^\n]+)\n        value: \{x: ([^,]+), y: ([^,]+), z: ([^}]+)\}\n        inSlope: \{x: ([^,]+), y: ([^,]+), z: ([^}]+)\}\n        outSlope: \{x: ([^,]+), y: ([^,]+), z: ([^}]+)\}')
            $axes = @()
            for ($axis = 0; $axis -lt 3; $axis++) {
                $values = foreach ($key in $keys) {
                    'new Keyframe(' + (@(1, (2+$axis), (5+$axis), (8+$axis) | ForEach-Object { $key.Groups[$_].Value + 'f' }) -join ', ') + ')'
                }
                $axes += 'new Keyframe[] { ' + ($values -join ', ') + ' }'
            }
            $text += '        new Track("' + $track.Groups[2].Value + '", ' + ($axes -join ', ') + "),`n"
            $trackCount++
        }
        $text += "    };`n"
    }
}
$text += "}`n"
if ($Check) {
    $actual = [IO.File]::ReadAllText($destination).Replace("`r`n", "`n")
    if ($actual.TrimEnd() -ne $text.TrimEnd()) { throw 'Baked robot-arm animation curves differ from their authored source. Run the baker without -Check.' }
    Write-Output "PASS: $trackCount animation tracks match their authored paths, keys, values and tangents."
} else {
    [IO.File]::WriteAllText($destination, $text, [Text.UTF8Encoding]::new($false))
    Write-Output "Baked $trackCount animation tracks to $destination"
}

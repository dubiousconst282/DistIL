# FuzzyBisect.ps1 <MaxSteps> <InputAsmPath> <OutputAsmPath> -- <DotnetArgs>

$bisectStr = "fuzzy:"

for ($step = 1; $step -le $args[0]; $step++) {
    foreach ($p in @(50, 25, 12, 4, 0)) {
        Write-Host "Attempt #$step - $bisectStr$p"

        & dotnet "../src/DistIL.Cli/bin/Debug/DistIL.Cli.dll" `
                -i $args[1] -o $args[2] `
                -r "C:/Program Files/dotnet/shared/Microsoft.NETCore.App/6.0.22" `
                --bisect "$bisectStr$p" `
                --verbosity warn

        $proc = Start-Process dotnet.exe -PassThru -WindowStyle Hidden -Wait -ArgumentList  $args[3..($args.Length-1)]
        Write-Host "ExitCode: $($proc.ExitCode)"

        if ($proc.ExitCode -ne 0) { 
            $bisectStr += $p.ToString() + ":"
            break;
        }
    }
}

Write-Host "Final bisect string: $bisectStr"
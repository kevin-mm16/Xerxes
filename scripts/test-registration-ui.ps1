param([ValidateSet('Accept','Blank','Register','Approve','Decline','Status')][string]$Action='Status')
$ErrorActionPreference='Stop'
Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes
$root=[System.Windows.Automation.AutomationElement]::RootElement
$title=if($Action -in @('Approve','Decline')){'MiLife IT support approval'}else{'MiLife | PC check-in'}
$window=$root.FindFirst([System.Windows.Automation.TreeScope]::Children,(New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty,$title)))
if(!$window){throw "Window unavailable: $title"}
function Find-Name([string]$name){$window.FindFirst([System.Windows.Automation.TreeScope]::Descendants,(New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty,$name)))}
if($Action -eq 'Accept'){
    $agreement=Find-Name 'I understand and accept this notice.'
    $agreement.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle()
    (Find-Name 'Accept and continue').GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
}
if($Action -in @('Blank','Register')){
    $box=Find-Name 'Your full name'
    $name=if($Action -eq 'Blank'){''}else{'IT Acceptance Test'}
    $box.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($name)
    (Find-Name 'Check in').GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
}
if($Action -in @('Approve','Decline')){
    $button=if($Action -eq 'Approve'){'Approve command'}else{'Decline'}
    (Find-Name $button).GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
}
if($Action -eq 'Status' -or $Action -eq 'Blank'){
    $window.FindAll([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.Condition]::TrueCondition)|ForEach-Object{$_.Current.Name}
}

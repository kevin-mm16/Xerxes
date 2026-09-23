param([ValidateSet('Blank','Register','Approve','Decline','Status')][string]$Action='Status')
$ErrorActionPreference='Stop'
Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes
$root=[System.Windows.Automation.AutomationElement]::RootElement
$title=if($Action -in @('Approve','Decline')){'MiLife IT support approval'}else{'miLife | PC check-in'}
$window=$root.FindFirst([System.Windows.Automation.TreeScope]::Children,(New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty,$title)))
if(!$window){throw "Window unavailable: $title"}
function Find-Name([string]$name){$window.FindFirst([System.Windows.Automation.TreeScope]::Descendants,(New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty,$name)))}
if($Action -in @('Blank','Register')){
    $box=$window.FindFirst([System.Windows.Automation.TreeScope]::Descendants,(New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty,[System.Windows.Automation.ControlType]::Edit)))
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

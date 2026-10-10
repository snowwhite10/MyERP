param([string]$TaskName = 'MyERP-AI-Runner')
Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false
Write-Host "已移除工作排程器任務：$TaskName"

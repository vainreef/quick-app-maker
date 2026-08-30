# V2 命令速查

```powershell
.\bootstrap\qam.cmd doctor
.\bootstrap\qam.cmd bootstrap
.\bootstrap\qam.cmd self-test
.\bootstrap\qam.cmd create --name "名称" --slug slug
.\bootstrap\qam.cmd dev .\slug
.\bootstrap\qam.cmd test .\slug
.\bootstrap\qam.cmd store launch --app .\slug
.\bootstrap\qam.cmd store reserve --app .\slug --name "名称"
.\bootstrap\qam.cmd package .\slug --profile store
.\bootstrap\qam.cmd store preflight --app .\slug
.\bootstrap\qam.cmd store discover --app .\slug
.\bootstrap\qam.cmd store run --app .\slug --apply --confirm-age-ratings --deadline 3600000
.\bootstrap\qam.cmd store apply --app .\slug --phase availability
.\bootstrap\qam.cmd store apply --app .\slug --phase properties
.\bootstrap\qam.cmd store apply --app .\slug --phase age-ratings --confirm-age-ratings
.\bootstrap\qam.cmd store apply --app .\slug --phase packages
.\bootstrap\qam.cmd store apply --app .\slug --phase listing
.\bootstrap\qam.cmd store apply --app .\slug --phase options
.\bootstrap\qam.cmd store verify --app .\slug
.\bootstrap\qam.cmd store status --app .\slug
.\bootstrap\qam.cmd store stop --app .\slug
```

`store plan` 为只读动作，发现差异返回 4；`store apply` 才执行写入。最终认证按钮由用户点击。

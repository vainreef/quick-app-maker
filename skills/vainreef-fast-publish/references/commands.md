# V2 命令速查

```powershell
node .\bin\qam.mjs doctor
node .\bin\qam.mjs bootstrap
node .\bin\qam.mjs create --name "名称" --slug slug
node .\bin\qam.mjs dev .\slug
node .\bin\qam.mjs test .\slug
node .\bin\qam.mjs store launch --app .\slug
node .\bin\qam.mjs store reserve --app .\slug --name "名称"
node .\bin\qam.mjs package .\slug --profile store
node .\bin\qam.mjs store preflight --app .\slug
node .\bin\qam.mjs store discover --app .\slug
node .\bin\qam.mjs store run --app .\slug --apply --confirm-age-ratings --deadline 3600000
node .\bin\qam.mjs store apply --app .\slug --phase availability
node .\bin\qam.mjs store apply --app .\slug --phase properties
node .\bin\qam.mjs store apply --app .\slug --phase age-ratings --confirm-age-ratings
node .\bin\qam.mjs store apply --app .\slug --phase packages
node .\bin\qam.mjs store apply --app .\slug --phase listing
node .\bin\qam.mjs store apply --app .\slug --phase options
node .\bin\qam.mjs store verify --app .\slug
node .\bin\qam.mjs store status --app .\slug
node .\bin\qam.mjs store stop --app .\slug
```

`store plan` 为只读动作，发现差异返回 4；`store apply` 才执行写入。最终认证按钮由用户点击。

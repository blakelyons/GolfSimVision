# Is APIversion casing checked? gspro-r10.exe sends the capital-V form 'APIVersion' and works -- is GSPro actually case-insensitive here?

**Yes -- both casings work.** First scenario of the successful `--scenarios` batch
(exp14+15+19+20, one connection, one reset up front). The spec-correct lowercase-v
`"APIversion":"1"` key got `200 "Ball Data received"`; the capital-V `"APIVersion":"1"`
key (what `gspro-r10.exe` actually sends) also got `200 "Ball Data received"`. GSPro
tolerates both -- not case-sensitive on this key, at least for the values tested.

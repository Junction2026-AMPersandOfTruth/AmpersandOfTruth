# Blind review: classifications of reverted Finnish Wikipedia edits

## Edit 1 — id `rev-400519`

- Article: Suomen sisällissota  
- When: 2005-08-31T10:51:37Z, anonymous editor, live for 24 s
- Edit summary: /* Sodan jälkeiset tapahtumat */  
- Revert summary: <user>:n (keskustelu) muokkaukset kumottiin ja artikkeli palautettiin viimeisimpään <user>:n tekemään versioon.

**REMOVED:**
```
(empty)
```
**INSERTED:**
```
maria lensi lentokoneella venäjän zaarin päältä ja srmasi verisesti muita
```
**CONTEXT (before the edit):**
```
…jo siksikään, että tuhansia niitä äänestäneitä oli kuollut teloituksissa ja leireillä tai oli heiltä poistettu "kansalaisluottamus" eli äänioikeus - rangaistuskeino, jota nyttemmin ei enää käytetä.

==Sodan uhrit==

{| border=1 cellpadding="2" cellspacing="0" align="right" style="text-align: right;"
|- 
! colspan="5" style="background-color: #cfcfcf; text-align: center;" | Sodan uhrit
|-…
```
**Output A:**
```json
{
 "what_changed_en": "An anonymous editor added a nonsensical sentence about someone named Maria flying over Russia and killing others.",
 "evidence": [
  "maria lensi lentokoneella venäjän zaarin päältä ja srmasi verisesti muita"
 ],
 "claim_en": null,
 "claim_fi": null,
 "narrative_en": null,
 "category": "vandalism",
 "confidence": 5,
 "historical_note_en": null,
 "uncertainty_note": null
}
```
**Output B:**
```json
{
 "what_changed_en": "Inserted an absurd, ungrammatical sentence claiming that \"maria flew by airplane over the Russian tsar and bloodily killed others\".",
 "evidence": [
  "maria lensi lentokoneella venäjän zaarin päältä ja srmasi verisesti muita"
 ],
 "claim_en": null,
 "claim_fi": null,
 "narrative_en": null,
 "category": "vandalism",
 "confidence": 5,
 "historical_note_en": null,
 "uncertainty_note": null
}
```
**Output C:**
```json
{
 "what_changed_en": "Inserted a nonsensical sentence claiming Maria flew an airplane over the Russian Czar and bloodily killed others.",
 "evidence": [
  "maria lensi lentokoneella venäjän zaarin päältä ja srmasi verisesti muita"
 ],
 "claim_en": null,
 "claim_fi": null,
 "narrative_en": null,
 "category": "vandalism",
 "confidence": 5,
 "historical_note_en": null,
 "uncertainty_note": null
}
```
**Output D:**
```json
{
 "what_changed_en": "Inserted a nonsensical, uncapitalised sentence with typos stating that 'maria flew an airplane over the Russian tsar and bloodily killed others'.",
 "evidence": [
  "maria lensi lentokoneella venäjän zaarin päältä ja srmasi verisesti muita"
 ],
 "claim_en": null,
 "claim_fi": null,
 "narrative_en": null,
 "category": "vandalism",
 "confidence": 5,
 "historical_note_en": null,
 "uncertainty_note": null
}
```
**Output E:**
```json
{
 "what_changed_en": "Inserted a nonsensical, poorly spelled joke sentence claiming that someone named Maria flew an airplane over the Russian Tsar and bloodily killed others.",
 "evidence": [
  "maria lensi lentokoneella venäjän zaarin päältä ja srmasi verisesti muita"
 ],
 "claim_en": null,
 "claim_fi": null,
 "narrative_en": null,
 "category": "vandalism",
 "confidence": 5,
 "historical_note_en": null,
 "uncertainty_note": null
}
```

## Edit 2 — id `rev-1580551`

- Article: Talvisota  
- When: 2006-09-04T06:29:36Z, anonymous editor, live for 13 s
- Edit summary: /* Venäläisen Puna-armeijan vahvuudet ja heikkoudet */  
- Revert summary: Käyttäjän <user> (keskustelu) muokkaukset kumottiin ja sivu palautettiin viimeisimpään käyttäjän <user> tekemään versioon.

**REMOVED:**
```
Alussa esimerkiksi panssariase ja jalkaväki eivät tukeneet toisiaan.
```
**INSERTED:**
```
Alussa esimerkiksi panssariase ja jalkaväkipa paska perse eivät tukeneet toisiaan.
```
**CONTEXT (before the edit):**
```
…suomalaisten ei uskottu pystyvän merkittäviin vastatoimiin, ja ei osattu olla varovaisia ja valppaita. Myös eri yksiköiden välisessä yhteistyössä oli joskus kitkaa. Alussa esimerkiksi panssariase ja jalkaväki eivät tukeneet toisiaan.
Puna-armeijan perusajatus oli hyökkäys, jonka alussa uskottiin onnistuvat lyhyessä ajassa.  
Puna-armeijalla ei ollut Suomeen hyökätessään talvivarusteita, armeijan…
```
**Output A:**
```json
{
 "what_changed_en": "Inserted the Finnish profanities \"paska perse\" (\"shit ass\") into a sentence about the cooperation between Red Army tank and infantry units.",
 "evidence": [
  "jalkaväkipa paska perse"
 ],
 "claim_en": null,
 "claim_fi": null,
 "narrative_en": null,
 "category": "vandalism",
 "confidence": 5,
 "historical_note_en": null,
 "uncertainty_note": null
}
```
**Output B:**
```json
{
 "what_changed_en": "The edit added vulgar slang ('pa paska perse') into the middle of a sentence discussing the Red Army's tactics.",
 "evidence": [
  "Alussa esimerkiksi panssariase ja jalkaväkipa paska perse eivät tukeneet toisiaan."
 ],
 "claim_en": null,
 "claim_fi": null,
 "narrative_en": null,
 "category": "vandalism",
 "confidence": 5,
 "historical_note_en": null,
 "uncertainty_note": null
}
```
**Output C:**
```json
{
 "what_changed_en": "Inserted profanity ('paska perse', meaning 'shit ass') into a sentence about the Red Army's infantry and armored units.",
 "evidence": [
  "jalkaväkipa paska perse"
 ],
 "claim_en": null,
 "claim_fi": null,
 "narrative_en": null,
 "category": "vandalism",
 "confidence": 5,
 "historical_note_en": null,
 "uncertainty_note": null
}
```
**Output D:**
```json
{
 "what_changed_en": "Inserted vulgar words ('pa paska perse', meaning 'shit ass') into a sentence discussing the Red Army's infantry and tank coordination.",
 "evidence": [
  "jalkaväkipa paska perse eivät tukeneet toisiaan."
 ],
 "claim_en": null,
 "claim_fi": null,
 "narrative_en": null,
 "category": "vandalism",
 "confidence": 5,
 "historical_note_en": null,
 "uncertainty_note": null
}
```
**Output E:**
```json
{
 "what_changed_en": "Inserted Finnish profanity (\"paska perse\", meaning \"shit ass\") and gibberish letters into the middle of a sentence about Red Army tanks and infantry.",
 "evidence": [
  "jalkaväkipa paska perse"
 ],
 "claim_en": null,
 "claim_fi": null,
 "narrative_en": null,
 "category": "vandalism",
 "confidence": 5,
 "historical_note_en": null,
 "uncertainty_note": null
}
```

## Edit 3 — id `rev-15144408`

- Article: Saamelaiset  
- When: 2015-08-17T08:27:22Z, anonymous editor, live for 1 min
- Edit summary: korjaamalla nimen kuntoon  
- Revert summary: Käyttäjän <user> (keskustelu) muokkaukset kumottiin ja sivu palautettiin viimeisimpään käyttäjän <user> tekemään versioon.

**REMOVED:**
```
| nimi            = Saamelaiset
```
**INSERTED:**
```
| nimi            = autistit vammaset paska määlyt
```
**CONTEXT (before the edit):**
```
{{Kansa
| nimi            = Saamelaiset
| lippu           = Sami_flag.svg
|  lipun leveys   = 250px
|  lipun selite   = [[Saamen lippu|Saamelaisten lippu]].
| kuvat           = {{image array|perrow=5
| image1  = Mari Boine, Nordiska radets musikpristagare (Bilden ar tagen vid Nordiska radets session i Oslo, 2003) (3).jpg | caption1 = [[Mari Boine]]
| image2 = Sofia Jannok 20070621 Centre Culturel…
```
**Output A:**
```json
{
 "what_changed_en": "Replaced the name of the Sámi people in the infobox with offensive slurs and insults ('autistic disabled shit fools').",
 "evidence": [
  "| nimi            = autistit vammaset paska määlyt",
  "korjaamalla nimen kuntoon"
 ],
 "claim_en": null,
 "claim_fi": null,
 "narrative_en": null,
 "category": "vandalism",
 "confidence": 5,
 "historical_note_en": null,
 "uncertainty_note": null
}
```
**Output B:**
```json
{
 "what_changed_en": "Replaced the name of the Sámi people in the infobox with derogatory slurs and insults ('autistit vammaset paska määlyt').",
 "evidence": [
  "| nimi            = autistit vammaset paska määlyt",
  "korjaamalla nimen kuntoon"
 ],
 "claim_en": null,
 "claim_fi": null,
 "narrative_en": null,
 "category": "vandalism",
 "confidence": 5,
 "historical_note_en": null,
 "uncertainty_note": null
}
```
**Output C:**
```json
{
 "what_changed_en": "The edit replaced the name of the Sami people in an infobox with an offensive slur and insults.",
 "evidence": [
  "| nimi            = autistit vammaset paska määlyt",
  "korjaamalla nimen kuntoon"
 ],
 "claim_en": null,
 "claim_fi": null,
 "narrative_en": null,
 "category": "vandalism",
 "confidence": 5,
 "historical_note_en": null,
 "uncertainty_note": null
}
```
**Output D:**
```json
{
 "what_changed_en": "Replaced the name \"Saamelaiset\" (Sami people) in the infobox with ableist slurs and profanity (\"autists disabled shit retards\"), accompanied by an edit summary claiming to \"fix the name\".",
 "evidence": [
  "| nimi            = Saamelaiset",
  "| nimi            = autistit vammaset paska määlyt",
  "korjaamalla nimen kuntoon"
 ],
 "claim_en": null,
 "claim_fi": null,
 "narrative_en": null,
 "category": "vandalism",
 "confidence": 5,
 "historical_note_en": null,
 "uncertainty_note": null
}
```
**Output E:**
```json
{
 "what_changed_en": "Replaced the name \"Saamelaiset\" (Sámi people) in the infobox with a string of ableist slurs and profanity (\"autistit vammaset paska määlyt\", roughly \"autistics disabled shit dumbasses\"), while claiming in the edit summary to be \"fixing the name\" (\"korjaamalla nimen kuntoon\").",
 "evidence": [
  "| nimi            = Saamelaiset",
  "| nimi            = autistit vammaset paska määlyt",
  "korjaamalla nimen kuntoon"
 ],
 "claim_en": null,
 "claim_fi": null,
 "narrative_en": null,
 "category": "vandalism",
 "confidence": 5,
 "historical_note_en": null,
 "uncertainty_note": null
}
```

## Edit 4 — id `rev-18648732`

- Article: Lapin sota  
- When: 2020-01-09T07:28:59Z, registered editor, live for 20 s
- Edit summary: Perkelle  
- Revert summary: Hylättiin viimeisin tekstimuutos (tehnyt <user>) ja palautettiin versio 18648730, jonka on tehnyt <user>

**REMOVED:**
```
'''Lapin sota''' käytiin osana [[toinen maailmansota|toista maailmansotaa]] [[Suomi|Suomen]] ja [[natsi-Saksa|Saksan]] välillä pääasiassa Suomen [[Lapin maakunta|Lapissa]] [[15.
```
**INSERTED:**
```
'''Lapin sota''' käytiin osana [[toinen maailmanorgio synti vuonna 98 ja siitä lähtien kakkoseen ja niin pantiin vain perkeleesti kännissä ja näin  |toista maailmansotaa]] [[Suomi|Suomen]] ja [[natsi-Saksa|Saksan]] välillä pääasiassa Suomen [[Lapin maakunta|Lapissa]] [[15.
```
**CONTEXT (before the edit):**
```
…/>264 kadonnutta<br />2&nbsp;904 haavoittunutta<ref name="tikkanen">Tikkanen 1996</ref>
| huomautus      = 
| muut_taistelut = Lapin sodan taistelut
}}

'''Lapin sota''' käytiin osana [[toinen maailmansota|toista maailmansotaa]] [[Suomi|Suomen]] ja [[natsi-Saksa|Saksan]] välillä pääasiassa Suomen [[Lapin maakunta|Lapissa]] [[15. syyskuuta]] [[1944]] – [[27. huhtikuuta]]…
```
**Output A:**
```json
{
 "what_changed_en": "Replaced the link target for World War II with obscene and nonsensical Finnish text, accompanied by a profane edit summary.",
 "evidence": [
  "toinen maailmanorgio synti vuonna 98 ja siitä lähtien kakkoseen ja niin pantiin vain perkeleesti kännissä ja näin",
  "Perkelle"
 ],
 "claim_en": null,
 "claim_fi": null,
 "narrative_en": null,
 "category": "vandalism",
 "confidence": 5,
 "historical_note_en": null,
 "uncertainty_note": null
}
```
**Output B:**
```json
{
 "what_changed_en": "Replaced the target of the link for 'World War II' with a long, vulgar gibberish phrase about an orgy, anal sex, and drunken swearing, and used a misspelled curse word ('Perkelle') as the edit summary.",
 "evidence": [
  "Perkelle",
  "[[toinen maailmanorgio synti vuonna 98 ja siitä lähtien kakkoseen ja niin pantiin vain perkeleesti kännissä ja näin  |toista maailmansotaa]]"
 ],
 "claim_en": null,
 "claim_fi": null,
 "narrative_en": null,
 "category": "vandalism",
 "confidence": 5,
 "historical_note_en": null,
 "uncertainty_note": null
}
```
**Output C:**
```json
{
 "what_changed_en": "Replaced the link target for World War II with vulgar and nonsensical sexual phrasing.",
 "evidence": [
  "toinen maailmanorgio synti vuonna 98 ja siitä lähtien kakkoseen ja niin pantiin vain perkeleesti kännissä ja näin",
  "Perkelle"
 ],
 "claim_en": null,
 "claim_fi": null,
 "narrative_en": null,
 "category": "vandalism",
 "confidence": 5,
 "historical_note_en": null,
 "uncertainty_note": null
}
```
**Output D:**
```json
{
 "what_changed_en": "Inserted vulgar and nonsensical text about orgies, anal sex, and drunkenly having sex into the link target for 'World War II', accompanied by a profanity ('Perkelle', a misspelling of 'perkele' / 'devil/damn') in the edit summary.",
 "evidence": [
  "toinen maailmanorgio synti vuonna 98 ja siitä lähtien kakkoseen ja niin pantiin vain perkeleesti kännissä ja näin",
  "Perkelle"
 ],
 "claim_en": null,
 "claim_fi": null,
 "narrative_en": null,
 "category": "vandalism",
 "confidence": 5,
 "historical_note_en": null,
 "uncertainty_note": null
}
```
**Output E:**
```json
{
 "what_changed_en": "The editor vandalized a wikilink by inserting obscene and nonsensical text into it.",
 "evidence": [
  "Perkelle",
  "toinen maailmanorgio synti vuonna 98 ja siitä lähtien kakkoseen ja niin pantiin vain perkeleesti kännissä ja näin"
 ],
 "claim_en": null,
 "claim_fi": null,
 "narrative_en": null,
 "category": "vandalism",
 "confidence": 5,
 "historical_note_en": null,
 "uncertainty_note": null
}
```

## Edit 5 — id `rev-23739766`

- Article: Simo Häyhä  
- When: 2025-12-23T18:12:13Z, temporary editor, live for 4 min
- Edit summary: (empty)  
- Revert summary: Käyttäjän <user> muokkaukset kumottiin ja sivu palautettiin viimeisimpään käyttäjän <user> tekemään versioon.

**REMOVED:**
```
| nimi = Simo Häyhä
…
| kuolinpaikka = [[Hamina]], Suomi
…
| ammatti = [[maanviljely|maanviljelijä]]
```
**INSERTED:**
```
| nimi = Simo on tyhmä
…
| kuolinpaikka = [[joku]], Suomi
…
| ammatti = [[maanviljeliä|maanviljelijä]]
```
**CONTEXT (before the edit):**
```
{{Lupaava}}
{{Henkilö/sotilashenkilö
| nimi = Simo Häyhä
| sotilasarvo = [[Vänrikki]]
| kuva = Mestariampuja Simo Häyhä "Saharan kauhun" komppaniasta, SAKuva-9479.jpg
| kuvateksti = Simo Häyhä [[talvisota|talvisodassa]] 17. [[helmikuu]]ta [[1940]].
| syntymäaika = {{Syntymäaika ja ikä|17|12|1905||1}}
| syntymäpaikka = [[Rautjärvi]], [[Suomen suuriruhtinaskunta]]
| kuolinaika = {{Kuolinaika ja…
```
**Output A:**
```json
{
 "what_changed_en": "Replaced Simo Häyhä's name in the infobox with 'Simo is stupid' ('Simo on tyhmä'), changed his place of death from Hamina to 'someone' ('joku'), and altered a link target to a misspelling.",
 "evidence": [
  "| nimi = Simo on tyhmä",
  "| kuolinpaikka = [[joku]], Suomi"
 ],
 "claim_en": null,
 "claim_fi": null,
 "narrative_en": null,
 "category": "vandalism",
 "confidence": 5,
 "historical_note_en": null,
 "uncertainty_note": null
}
```
**Output B:**
```json
{
 "what_changed_en": "Replaced Simo Häyhä's name in the infobox with an insult ('Simo is stupid'), changed his place of death from Hamina to 'someone' ('joku'), and broke the link target for his profession ('farmer').",
 "evidence": [
  "| nimi = Simo on tyhmä",
  "| kuolinpaikka = [[joku]], Suomi",
  "| ammatti = [[maanviljeliä|maanviljelijä]]"
 ],
 "claim_en": null,
 "claim_fi": null,
 "narrative_en": null,
 "category": "vandalism",
 "confidence": 5,
 "historical_note_en": null,
 "uncertainty_note": null
}
```
**Output C:**
```json
{
 "what_changed_en": "The editor vandalized the infobox for Simo Häyhä by changing his name to 'Simo on tyhmä' (Simo is stupid) and his place of death to 'joku' (someone/somewhere).",
 "evidence": [
  "| nimi = Simo on tyhmä",
  "| kuolinpaikka = [[joku]], Suomi"
 ],
 "claim_en": null,
 "claim_fi": null,
 "narrative_en": null,
 "category": "vandalism",
 "confidence": 5,
 "historical_note_en": null,
 "uncertainty_note": null
}
```
**Output D:**
```json
{
 "what_changed_en": "Replaced Simo Häyhä's name in the infobox with 'Simo on tyhmä' ('Simo is stupid'), changed his place of death from Hamina to 'joku' ('someone/something'), and broke the link for his occupation.",
 "evidence": [
  "| nimi = Simo on tyhmä",
  "| kuolinpaikka = [[joku]], Suomi",
  "| ammatti = [[maanviljeliä|maanviljelijä]]"
 ],
 "claim_en": null,
 "claim_fi": null,
 "narrative_en": null,
 "category": "vandalism",
 "confidence": 5,
 "historical_note_en": null,
 "uncertainty_note": null
}
```
**Output E:**
```json
{
 "what_changed_en": "Replaced Simo Häyhä's name with an insult calling him stupid (\"Simo on tyhmä\") and changed his place of death to \"someone\" (\"joku\").",
 "evidence": [
  "| nimi = Simo on tyhmä",
  "| kuolinpaikka = [[joku]], Suomi"
 ],
 "claim_en": null,
 "claim_fi": null,
 "narrative_en": null,
 "category": "vandalism",
 "confidence": 5,
 "historical_note_en": null,
 "uncertainty_note": null
}
```

## Edit 6 — id `rev-2968411`

- Article: Talvisota  
- When: 2007-06-06T04:48:12Z, anonymous editor, live for 3 h 38 min
- Edit summary: (empty)  
- Revert summary: Käyttäjän <user> (keskustelu) muokkaus 2968411 kumottiin

**REMOVED:**
```
(empty)
```
**INSERTED:**
```
Sota alkoi kun fasistinen Suomi hyokkäsi Neuvostoliittoon.
```
**CONTEXT (before the edit):**
```
…| muut_taistelut = Talvisodan taistelut
}}
'''Talvisota''' oli [[30. marraskuuta]] [[1939]]–[[13. maaliskuuta]] [[1940]] [[Suomi|Suomen]] ja [[Neuvostoliitto|Neuvostoliiton]] välillä käyty [[sota]]. Konflikti oli osa [[toinen maailmansota|toista maailmansotaa]]. Sota päättyi [[Moskovan rauha]]nsopimukseen, jossa Suomi menetti 10 prosenttia maa-alueistaan ja toiseksi suurimman kaupunkinsa,…
```
**Output A:**
```json
{
 "what_changed_en": "Inserted a sentence claiming that the Winter War began when 'fascist Finland attacked the Soviet Union'.",
 "evidence": [
  "Sota alkoi kun fasistinen Suomi hyokkäsi Neuvostoliittoon."
 ],
 "claim_en": "The Winter War began because fascist Finland attacked the Soviet Union.",
 "claim_fi": "Talvisota alkoi, kun fasistinen Suomi hyökkäsi Neuvostoliittoon.",
 "narrative_en": "Soviet propaganda narrative on the Winter War",
 "category": "pov_push",
 "confidence": 4,
 "historical_note_en": "The Winter War began on 30 November 1939 with an unprovoked Soviet invasion of Finland following the staged shelling of Mainila.",
 "uncertainty_note": "Could also be classified as simple trolling/vandalism given the bluntness of the insertion."
}
```
**Output B:**
```json
{
 "what_changed_en": "Inserted a sentence into the lead section claiming that the Winter War began when \"fascist Finland attacked the Soviet Union\" (with a typo in \"hyökkäsi\"), pushing both a false factual claim and ideological spin.",
 "evidence": [
  "Sota alkoi kun fasistinen Suomi hyokkäsi Neuvostoliittoon."
 ],
 "claim_en": "The Winter War began when fascist Finland attacked the Soviet Union.",
 "claim_fi": "Talvisota alkoi, kun fasistinen Suomi hyökkäsi Neuvostoliittoon.",
 "narrative_en": "Soviet propaganda justification of the Winter War",
 "category": "false_factual_claim",
 "confidence": 4,
 "historical_note_en": "The Winter War began on 30 November 1939 when the Soviet Union invaded parliamentary-democratic Finland following the Soviet-staged Shelling of Mainila.",
 "uncertainty_note": "It is ambiguous whether this anonymous edit was sincere ideological propaganda/POV-pushing or deliberate trolling/vandalism mimicking Soviet historiography, though the direct assertion of who attacked whom best fits false_factual_claim."
}
```
**Output C:**
```json
{
 "what_changed_en": "The edit added a claim that the Winter War started when fascist Finland attacked the Soviet Union.",
 "evidence": [
  "Sota alkoi kun fasistinen Suomi hyokkäsi Neuvostoliittoon."
 ],
 "claim_en": "The Winter War began because fascist Finland attacked the Soviet Union.",
 "claim_fi": "Talvisota alkoi, kun fasistinen Suomi hyökkäsi Neuvostoliittoon.",
 "narrative_en": "Soviet justification of the Winter War",
 "category": "pov_push",
 "confidence": 5,
 "historical_note_en": "The Winter War began with a Soviet invasion of Finland, not a Finnish attack on the Soviet Union.",
 "uncertainty_note": null
}
```
**Output D:**
```json
{
 "what_changed_en": "Inserted a claim into the opening paragraph stating that the war began when 'fascist Finland attacked the Soviet Union' ('Sota alkoi kun fasistinen Suomi hyokkäsi Neuvostoliittoon'). This combines a false factual claim about who initiated the attack with ideological POV labeling.",
 "evidence": [
  "Sota alkoi kun fasistinen Suomi hyokkäsi Neuvostoliittoon."
 ],
 "claim_en": "The Winter War began when fascist Finland attacked the Soviet Union.",
 "claim_fi": "Talvisota alkoi, kun fasistinen Suomi hyökkäsi Neuvostoliittoon.",
 "narrative_en": "Soviet propaganda narrative blaming Finland for starting the Winter War",
 "category": "false_factual_claim",
 "confidence": 5,
 "historical_note_en": "The Winter War began on 30 November 1939 with a Soviet invasion of Finland following the Soviet-staged Shelling of Mainila; Finland was a parliamentary democracy, not a fascist state.",
 "uncertainty_note": "The edit could also be classified as pov_push due to the ideological term 'fascist' ('fasistinen') or as trolling/vandalism, but its primary function in the text is asserting a false historical cause for the war."
}
```
**Output E:**
```json
{
 "what_changed_en": "Inserted a sentence claiming that the Winter War began when 'fascist Finland' attacked the Soviet Union.",
 "evidence": [
  "Sota alkoi kun fasistinen Suomi hyokkäsi Neuvostoliittoon."
 ],
 "claim_en": "The Winter War began when fascist Finland attacked the Soviet Union.",
 "claim_fi": "Talvisota alkoi, kun fasistinen Suomi hyökkäsi Neuvostoliittoon.",
 "narrative_en": "Soviet justification of the Winter War",
 "category": "false_factual_claim",
 "confidence": 5,
 "historical_note_en": "The Winter War began on 30 November 1939 when the Soviet Union invaded Finland.",
 "uncertainty_note": null
}
```

## Edit 7 — id `rev-4225630`

- Article: Talvisota  
- When: 2008-02-03T16:54:16Z, registered editor, live for 22 min
- Edit summary: (empty)  
- Revert summary: Käyttäjän <user> (keskustelu) muokkaukset kumottiin ja sivu palautettiin viimeisimpään käyttäjän <user> tekemään versioon.

**REMOVED:**
```
| vahvuus1 = 250 000 miestä, <br>30 tankkia ja<br>130 lentokonetta<ref name="Virrankoski">Pentti Virrankoski, ''Suomen Historia 2'', 2001, ISBN 951-746-342-1, SKS</ref><ref name="Käkelä">Erkki Käkelä, ''Laguksen miehet, Marskin nyrkki: Suomalainen panssariyhtymä 1941-1944'', 1992, ISBN 952-90-3858-5, Panssarikilta</ref>
…
| tappiot1 = 26 662 kuoli,<br>39 886 haavoittui ja <br>1 000 jäi vangiksi<ref>Maanpuolustuskorkeakoulu, ''Talvisodan historia 4'', s.406, 1991, ISBN 951-0-17566-8, WSOY, Kaatuneiden luku sisältää 3671 vakavasti haavoittunutta, jotka kuolivat pääsemättä koskaan sairaalasta jopa vuosia sodan jälkeen.</ref>
 | tappiot2 = 126 875 kuoli tai katosi (arvio),<br>264 908 haavoittui ja<br>3 100 jäi vangiksi<ref>G. F. Krivošejev, ''Soviet Casualties and Combat Losses in the Twentieth Century'', 1997, ISBN 1-85367-280-7, Greenhill Books</ref>
```
**INSERTED:**
```
| vahvuus1 = 337 000 miestä, <br>30 tankkia ja<br>130 lentokonetta<ref name="Virrankoski">Pentti Virrankoski, ''Suomen Historia 2'', 2001, ISBN 951-746-342-1, SKS</ref><ref name="Käkelä">Erkki Käkelä, ''Laguksen miehet, Marskin nyrkki: Suomalainen panssariyhtymä 1941-1944'', 1992, ISBN 952-90-3858-5, Panssarikilta</ref>
…
| tappiot1 = 26 662 kuoli,<br>44 886 haavoittui ja <br>1 000 jäi vangiksi<ref>Maanpuolustuskorkeakoulu, ''Talvisodan historia 4'', s.406, 1991, ISBN 951-0-17566-8, WSOY, Kaatuneiden luku sisältää 3671 vakavasti haavoittunutta, jotka kuolivat pääsemättä koskaan sairaalasta jopa vuosia sodan jälkeen.</ref>
 | tappiot2 = 126 875 kuoli tai katosi (arvio),<br>264 908 haavoittui ja<br>5 100 jäi vangiksi<ref>G. F. Krivošejev, ''Soviet Casualties and Combat Losses in the Twentieth Century'', 1997, ISBN 1-85367-280-7, Greenhill Books</ref>
…
Talvisotakirja kustantaja gummerrus ISBN 951-20-6674-2
```
**CONTEXT (before the edit):**
```
…G. E. Mannerheim]] 
 | komentaja2 = [[Kuva:Flag of the Soviet Union 1923.svg|25px]] [[Kliment Vorošilov]], myöhemmin <br> [[Kuva:Flag of the Soviet Union 1923.svg|25px]] [[Semjon Timošenko]]
 | vahvuus1 = 250 000 miestä, <br>30 tankkia ja<br>130 lentokonetta<ref name="Virrankoski">Pentti Virrankoski, ''Suomen Historia 2'', 2001, ISBN 951-746-342-1, SKS</ref><ref name="Käkelä">Erkki…
```
**Output A:**
```json
{
 "what_changed_en": "Changed the infobox figures for the Winter War—increasing Finnish troop strength from 250,000 to 337,000, Finnish wounded from 39,886 to 44,886, and Soviet prisoners of war from 3,100 to 5,100—while leaving the existing inline citations attached to the altered numbers and appending a poorly formatted book reference ('Talvisotakirja', publisher Gummerus) elsewhere in the article.",
 "evidence": [
  "| vahvuus1 = 337 000 miestä,",
  "44 886 haavoittui ja",
  "5 100 jäi vangiksi",
  "Talvisotakirja kustantaja gummerrus ISBN 951-20-6674-2"
 ],
 "claim_en": "In the Winter War, Finland's troop strength was 337,000 men, 44,886 Finnish soldiers were wounded, and 5,100 Soviet soldiers were taken prisoner.",
 "claim_fi": "Talvisodassa Suomen vahvuus oli 337 000 miestä, suomalaisia haavoittui 44 886 ja neuvostoliittolaisia jäi vangiksi 5 100.",
 "narrative_en": null,
 "category": "good_faith_error",
 "confidence": 4,
 "historical_note_en": null,
 "uncertainty_note": "Although changing sourced numbers without updating the inline citations creates a source-text mismatch (source_manipulation), the numbers themselves (337,000 peak Finnish strength, ~43,500–45,000 wounded, ~5,500 Soviet POWs) correspond to real estimates found in Winter War literature, and the clumsy attempt to add a Gummerus book with an ISBN strongly suggests an inexperienced editor acting in good faith rather than deliberate manipulation."
}
```
**Output B:**
```json
{
 "what_changed_en": "Changed the infobox figures for Finnish troop strength (from 250,000 to 337,000), Finnish wounded (from 39,886 to 44,886), and Soviet prisoners of war (from 3,100 to 5,100) while leaving the existing inline citations attached to the altered numbers, and appended an unformatted book reference (\"Talvisotakirja kustantaja gummerrus ISBN 951-20-6674-2\").",
 "evidence": [
  "vahvuus1 = 337 000 miestä,",
  "44 886 haavoittui",
  "5 100 jäi vangiksi",
  "Talvisotakirja kustantaja gummerrus ISBN 951-20-6674-2"
 ],
 "claim_en": "During the Winter War, Finland's troop strength was 337,000 men, 44,886 Finns were wounded, and 5,100 Soviet soldiers were taken prisoner.",
 "claim_fi": "Talvisodassa Suomen vahvuus oli 337 000 miestä, suomalaisia haavoittui 44 886 ja neuvostoliittolaisia jäi vangiksi 5 100.",
 "narrative_en": null,
 "category": "good_faith_error",
 "confidence": 4,
 "historical_note_en": null,
 "uncertainty_note": "The editor appears to have updated the numbers based on a real book (ISBN 951-20-6674-2 corresponds to Jari Leskinen & Antti Juutilainen's 'Talvisodan pikkujättiläinen' published by WSOY, or a similar Gummerus work), where ~337,000 represents peak field army strength at the end of the war rather than initial strength, and ~5,500 is a common estimate for Soviet POWs. However, by changing the numbers in place, the edit made the existing inline citations (Virrankoski, Talvisodan historia, Krivosheev) misrepresent their sources, so it also borders on source_manipulation or content_dispute."
}
```
**Output C:**
```json
{
 "what_changed_en": "Modified Finnish troop strength from 250,000 to 337,000, Finnish wounded from 39,886 to 44,886, and Soviet prisoners of war from 3,100 to 5,100 in the infobox, appending an unformatted book reference.",
 "evidence": [
  "| vahvuus1 = 337 000 miestä,",
  "44 886 haavoittui",
  "5 100 jäi vangiksi",
  "Talvisotakirja kustantaja gummerrus ISBN 951-20-6674-2"
 ],
 "claim_en": "Finnish troop strength was 337,000, Finnish wounded numbered 44,886, and 5,100 Soviet soldiers were captured in the Winter War.",
 "claim_fi": "Suomen joukkojen vahvuus oli 337 000 miestä, suomalaisia haavoittui 44 886 ja neuvostosotilaita jäi vangiksi 5 100 talvisodassa.",
 "narrative_en": null,
 "category": "good_faith_error",
 "confidence": 4,
 "historical_note_en": null,
 "uncertainty_note": "The editor appeared to attempt to update casualty and troop figures using a source ('Talvisotakirja', Gummerus), but placed the citation poorly and left existing citations attached to conflicting numbers."
}
```
**Output D:**
```json
{
 "what_changed_en": "The edit changed Finnish troop strength from 250,000 to 337,000, wounded numbers from 39,886 to 44,886, Soviet prisoners from 3,100 to 5,100, and added a book reference.",
 "evidence": [
  "337 000 miestä",
  "44 886 haavoittui",
  "5 100 jäi vangiksi",
  "Talvisotakirja kustantaja gummerrus ISBN 951-20-6674-2"
 ],
 "claim_en": "Finland had 337,000 men, 44,886 wounded, and 5,100 Soviet prisoners during the Winter War.",
 "claim_fi": "Talvisodassa Suomella oli 337 000 miestä, 44 886 haavoittunutta ja 5 100 neuvostovankia.",
 "narrative_en": null,
 "category": "false_factual_claim",
 "confidence": 4,
 "historical_note_en": null,
 "uncertainty_note": "The numbers might reflect a different source or scope of mobilization, but without proper citation or explanation they alter established infobox statistics."
}
```
**Output E:**
```json
{
 "what_changed_en": "Changed the Finnish strength from 250,000 to 337,000 men, Finnish wounded from 39,886 to 44,886, and Soviet prisoners of war from 3,100 to 5,100 in the infobox, while keeping the original citations and adding an unformatted book reference at the end.",
 "evidence": [
  "| vahvuus1 = 337 000 miestä,",
  "44 886 haavoittui",
  "5 100 jäi vangiksi",
  "Talvisotakirja kustantaja gummerrus ISBN 951-20-6674-2"
 ],
 "claim_en": "In the Winter War, Finland had 337,000 soldiers and 44,886 wounded, and 5,100 Soviet soldiers were taken prisoner.",
 "claim_fi": "Talvisodassa Suomella oli 337 000 sotilasta ja 44 886 haavoittunutta, ja 5 100 neuvostoliittolaista sotilasta jäi vangiksi.",
 "narrative_en": null,
 "category": "good_faith_error",
 "confidence": 4,
 "historical_note_en": null,
 "uncertainty_note": "It is possible the editor was trying to update figures from the cited Gummerus book, which makes it a good-faith error rather than deliberate falsification."
}
```

## Edit 8 — id `rev-1507690`

- Article: Karjalankannas  
- When: 2006-08-17T17:34:33Z, registered editor, live for 3 min
- Edit summary: Karjalankannas EI ole ollut osa (Neuvosto-/Venäjän) Karjalaa vuodesta 1945!  
- Revert summary: Sille voitaneen sitten esittää lähde. Koko Karjalaan se kuuluu

**REMOVED:**
```
Karjalankannas liitettiin osaksi [[Leningradin alue]]tta
…
Karjalankannas on Neuvostoliiton ja Venäjän hallussa ollessaan taantunut ja nykyään alueen elintaso ja vauraus on Venäjänkin mittapuulla heikko.
```
**INSERTED:**
```
koko Karjalankannas lakkasi olemasta osa [[Karjala]]a, tarkemmin sanottuna venäläisten hallitsemaa [[Karjalan tasavalta|itäistä Karjalaa]], kun se tuli osaksi nykyistä [[Pietari (kaupunki)|Pietari]]a ympäröivää [[Leningradin alue]]tta. Nimi Karjalankannas on kuitenkin säilynyt, sillä sitä myöden aikoinaan tulivat itäiset suomalaiset, lähinnä karjalan heimo Suomeen. Nykyään lyhin maayhteyskin Pietarista Venäjän Karjalaan kulkee juuri Kannaksen kautta
…
Karjalankannas on Neuvostoliiton ja Venäjän hallussa ollessaan taantunut ja nykyään alueen elintaso ja vauraus on Venäjänkin mittapuulla heikko.
```
**CONTEXT (before the edit):**
```
…suomalaisjoukot joutuivat perääntymään Etelä- ja Länsi-Kannakselta. Lisäksi Suomi joutui vetäytymään Itä-Kannakselta [[Moskovan välirauha]]nsopimuksen tultua voimaan syksyllä [[1944]].

Vuonna 1945 Karjalankannas liitettiin osaksi [[Leningradin alue]]tta. Vuonna [[1948]] lähes kaikki Kannaksen suomalaiset paikannimet korvattiin venäläisillä uusnimillä, joilla ei ollut muutamaa tapausta lukuun…
```
**Output A:**
```json
{
 "what_changed_en": "Expanded the statement that the Karelian Isthmus was annexed to Leningrad Oblast in 1945 to assert that it thereby ceased to be part of Karelia altogether, adding unsourced historical explanations for why its name persisted.",
 "evidence": [
  "Karjalankannas EI ole ollut osa (Neuvosto-/Venäjän) Karjalaa vuodesta 1945!",
  "koko Karjalankannas lakkasi olemasta osa [[Karjala]]a, tarkemmin sanottuna venäläisten hallitsemaa [[Karjalan tasavalta|itäistä Karjalaa]], kun se tuli osaksi nykyistä [[Pietari (kaupunki)|Pietari]]a ympäröivää [[Leningradin alue]]tta.",
  "Sille voitaneen sitten esittää lähde. Koko Karjalaan se kuuluu"
 ],
 "claim_en": "The Karelian Isthmus ceased to be part of Karelia in 1945 when it was incorporated into Leningrad Oblast rather than the Republic of Karelia.",
 "claim_fi": "Karjalankannas lakkasi olemasta osa Karjalaa vuonna 1945, kun se liitettiin Leningradin alueeseen eikä Karjalan tasavaltaan.",
 "narrative_en": null,
 "category": "good_faith_error",
 "confidence": 4,
 "historical_note_en": "While the Karelian Isthmus is administratively part of Leningrad Oblast rather than the Republic of Karelia, it remains universally considered a core part of the historical and geographical region of Karelia.",
 "uncertainty_note": "Could also be classified as content_dispute over geographical vs. administrative definitions of 'Karelia', or false_factual_claim since conflating the historical region with the Russian federal subject misstates geographical convention."
}
```
**Output B:**
```json
{
 "what_changed_en": "The editor added an assertion that the Karelian Isthmus ceased to be part of Karelia when it became part of Leningrad Oblast in 1945, along with folk-etymological speculation about the origin of its name.",
 "evidence": [
  "Karjalankannas EI ole ollut osa (Neuvosto-/Venäjän) Karjalaa vuodesta 1945!",
  "koko Karjalankannas lakkasi olemasta osa [[Karjala]]a",
  "Sille voitaneen sitten esittää lähde. Koko Karjalaan se kuuluu"
 ],
 "claim_en": "The Karelian Isthmus ceased to be part of Karelia in 1945 upon its annexation into Leningrad Oblast.",
 "claim_fi": "Karjalankannas lakkasi olemasta osa Karjalaa vuonna 1945, kun se liitettiin Leningradin alueeseen.",
 "narrative_en": null,
 "category": "content_dispute",
 "confidence": 4,
 "historical_note_en": null,
 "uncertainty_note": "Could be considered a good_faith_error due to conflating administrative boundaries (Leningrad Oblast vs. Republic of Karelia) with the wider historical and geographic region of Karelia."
}
```
**Output C:**
```json
{
 "what_changed_en": "The editor modified text to argue that the Karelian Isthmus ceased to be part of Karelia after 1945 because it was incorporated into the Leningrad Oblast instead of the Republic of Karelia.",
 "evidence": [
  "Karjalankannas EI ole ollut osa (Neuvosto-/Venäjän) Karjalaa vuodesta 1945!",
  "koko Karjalankannas lakkasi olemasta osa [[Karjala]]a, tarkemmin sanottuna venäläisten hallitsemaa [[Karjalan tasavalta|itäistä Karjalaa]], kun se tuli osaksi nykyistä [[Pietari (kaupunki)|Pietari]]a ympäröivää [[Leningradin alue]]tta."
 ],
 "claim_en": "The Karelian Isthmus is no longer part of Karelia since 1945 because it was attached to the Leningrad Oblast.",
 "claim_fi": "Karjalankannas ei ole ollut osa Karjalaa vuodesta 1945 lähtien, koska se liitettiin Leningradin alueeseen.",
 "narrative_en": "Geographical and administrative definition of the Karelian Isthmus",
 "category": "content_dispute",
 "confidence": 4,
 "historical_note_en": null,
 "uncertainty_note": "The dispute centers on historical versus current administrative definitions of regional names."
}
```
**Output D:**
```json
{
 "what_changed_en": "Expanded the statement that the Karelian Isthmus was annexed into Leningrad Oblast in 1945 by asserting that it therefore ceased to be part of Karelia (specifically the Republic of Karelia), while adding unsourced commentary on why the name survived and on road connections.",
 "evidence": [
  "koko Karjalankannas lakkasi olemasta osa [[Karjala]]a, tarkemmin sanottuna venäläisten hallitsemaa [[Karjalan tasavalta|itäistä Karjalaa]]",
  "Karjalankannas EI ole ollut osa (Neuvosto-/Venäjän) Karjalaa vuodesta 1945!",
  "Sille voitaneen sitten esittää lähde. Koko Karjalaan se kuuluu"
 ],
 "claim_en": "In 1945, the Karelian Isthmus ceased to be part of Karelia when it was administratively incorporated into the Leningrad Oblast rather than the Republic of Karelia.",
 "claim_fi": "Vuonna 1945 Karjalankannas lakkasi olemasta osa Karjalaa, kun se liitettiin Leningradin alueeseen eikä Karjalan tasavaltaan.",
 "narrative_en": null,
 "category": "content_dispute",
 "confidence": 4,
 "historical_note_en": "While the Karelian Isthmus was administratively transferred from the Karelo-Finnish SSR to the Leningrad Oblast in 1944–1945, in Finnish geography and historiography it remains a core part of the historical-cultural region of Karelia.",
 "uncertainty_note": "Could also be classified as good_faith_error because the editor conflates the administrative Republic of Karelia with the broader geographical-historical region of Karelia and adds unsourced claims about tribal migrations."
}
```
**Output E:**
```json
{
 "what_changed_en": "Replaced the statement that the Karelian Isthmus was annexed to Leningrad Oblast with an unsourced claim that it ceased to be part of Karelia (specifically Russian-ruled eastern Karelia) in 1945, adding an explanation about the origin of its name and its role as a land route.",
 "evidence": [
  "Karjalankannas EI ole ollut osa (Neuvosto-/Venäjän) Karjalaa vuodesta 1945!",
  "Sille voitaneen sitten esittää lähde. Koko Karjalaan se kuuluu",
  "koko Karjalankannas lakkasi olemasta osa [[Karjala]]a, tarkemmin sanottuna venäläisten hallitsemaa [[Karjalan tasavalta|itäistä Karjalaa]]"
 ],
 "claim_en": "The Karelian Isthmus ceased to be part of Karelia when it was incorporated into Leningrad Oblast in 1945.",
 "claim_fi": "Karjalankannas lakkasi olemasta osa Karjalaa, kun se liitettiin Leningradin alueeseen vuonna 1945.",
 "narrative_en": null,
 "category": "content_dispute",
 "confidence": 4,
 "historical_note_en": null,
 "uncertainty_note": "Could also be viewed as a good-faith error due to the unsourced, essay-like additions, though the edit summaries show a clear disagreement over terminology."
}
```

## Edit 9 — id `rev-13268224`

- Article: Talvisota  
- When: 2013-05-20T04:00:44Z, registered editor, live for 2 min
- Edit summary: /* Rauha */  
- Revert summary: Kumottu muokkaus 13268224, jonka teki <user> (keskustelu) toinen kerta

**REMOVED:**
```
Keskityksillä ei ollut mitään suoraa sotilaallista päämäärää, ja ne onkin tulkittu puhtaaksi kostotoimeksi.
```
**INSERTED:**
```
Keskityksillä ei ollut mitään suoraa sotilaallista päämäärää, ja ne olivatkin puhdas kostotoimi.
```
**CONTEXT (before the edit):**
```
…klo 11 alkaen. Kello 10:45 neuvostojoukkojen tykistö ja kranaatinheittimistö avasivat yllättäen tulen suomalaisten asemiin. Keskityksillä ei ollut mitään suoraa sotilaallista päämäärää, ja ne onkin tulkittu puhtaaksi kostotoimeksi. Tulituksen johdosta satoja suomalaisia kuoli tai haavoittui.<ref>Trotter 2000: 261.</ref> Sallan rintamalla loppuhetken tykistökeskitys oli ankaraa ja vielä…
```
**Output A:**
```json
{
 "what_changed_en": "Changed the description of the final Soviet artillery barrage before the Winter War ceasefire from saying it 'has been interpreted as' a pure act of revenge to asserting as an undisputed fact that it 'was' a pure act of revenge.",
 "evidence": [
  "ne onkin tulkittu puhtaaksi kostotoimeksi.",
  "ne olivatkin puhdas kostotoimi.",
  "toinen kerta"
 ],
 "claim_en": "The final Soviet artillery barrages right before the Winter War ceasefire were definitively a pure act of revenge.",
 "claim_fi": "Talvisodan loppuhetkien neuvostojoukkojen tykistökeskitykset olivat puhdas kostotoimi.",
 "narrative_en": "Emphasising Soviet malice in the Winter War",
 "category": "pov_push",
 "confidence": 4,
 "historical_note_en": null,
 "uncertainty_note": "Borders on content_dispute over encyclopedic tone and attribution, though stating inferred enemy motives as absolute fact pushes a non-neutral framing and was persistently re-inserted ('second time')."
}
```
**Output B:**
```json
{
 "what_changed_en": "The editor changed a speculative interpretation about Soviet artillery concentrations being interpreted as pure revenge into a definitive statement that they were pure revenge.",
 "evidence": [
  "ja ne onkin tulkittu puhtaaksi kostotoimeksi.",
  "ja ne olivatkin puhdas kostotoimi."
 ],
 "claim_en": "The Soviet artillery concentrations had no direct military goal and were purely an act of revenge.",
 "claim_fi": "Neuvostojoukkojen keskityksillä ei ollut mitään suoraa sotilaallista päämäärää, ja ne olivat puhdas kostotoimi.",
 "narrative_en": "Definitive framing of Soviet actions as revenge",
 "category": "pov_push",
 "confidence": 4,
 "historical_note_en": null,
 "uncertainty_note": "The change shifts nuanced academic phrasing to a definitive claim, which borders on a content dispute or POV push."
}
```
**Output C:**
```json
{
 "what_changed_en": "Changed the description of Soviet artillery strikes at the end of the war from having been 'interpreted as a pure act of revenge' to stating as a direct fact that they 'were a pure act of revenge'.",
 "evidence": [
  "ne onkin tulkittu puhtaaksi kostotoimeksi.",
  "ne olivatkin puhdas kostotoimi."
 ],
 "claim_en": "The final Soviet artillery barrages of the Winter War were an act of revenge, rather than merely interpreted as such.",
 "claim_fi": "Talvisodan viime hetken neuvostotykistökeskitykset olivat puhdas kostotoimi, eikä niitä vain ole tulkittu sellaiseksi.",
 "narrative_en": "Soviet malice in the Winter War",
 "category": "pov_push",
 "confidence": 4,
 "historical_note_en": null,
 "uncertainty_note": "Could also be classified as a content dispute or style dispute over encyclopedic hedging versus definitive phrasing."
}
```
**Output D:**
```json
{
 "what_changed_en": "Changed the statement that Soviet artillery barrages just before the ceasefire 'have been interpreted as a pure act of revenge' to state as an undisputed fact that they 'were a pure act of revenge'.",
 "evidence": [
  "ja ne onkin tulkittu puhtaaksi kostotoimeksi.",
  "ja ne olivatkin puhdas kostotoimi."
 ],
 "claim_en": "The Soviet artillery barrages just before the Winter War ceasefire were a pure act of revenge.",
 "claim_fi": "Neuvostojoukkojen tykistökeskitykset juuri ennen talvisodan aselepoa olivat puhdas kostotoimi.",
 "narrative_en": null,
 "category": "pov_push",
 "confidence": 4,
 "historical_note_en": null,
 "uncertainty_note": "Could also be viewed as a minor content_dispute over attribution and hedging."
}
```
**Output E:**
```json
{
 "what_changed_en": "Changed the description of the final Soviet artillery barrages just before the ceasefire from having been \"interpreted as a pure act of revenge\" to stating as fact that they \"were a pure act of revenge\".",
 "evidence": [
  "ne onkin tulkittu puhtaaksi kostotoimeksi.",
  "ne olivatkin puhdas kostotoimi.",
  "toinen kerta"
 ],
 "claim_en": "The final Soviet artillery barrages just before the Winter War ceasefire were a pure act of revenge.",
 "claim_fi": "Talvisodan loppuhetken neuvostoliittolaiset tykistökeskitykset olivat puhdas kostotoimi.",
 "narrative_en": "Anti-Soviet framing of the Winter War",
 "category": "pov_push",
 "confidence": 4,
 "historical_note_en": null,
 "uncertainty_note": "Borders on a content_dispute over encyclopedic tone (stating a historian's interpretation of motive in Wikipedia's voice vs. attributing it as an interpretation), though the revert summary notes it was the editor's second attempt."
}
```

## Edit 10 — id `rev-16351052`

- Article: Suomen sisällissota  
- When: 2017-03-15T20:47:19Z, anonymous editor, live for 3 h 9 min
- Edit summary: (empty)  
- Revert summary: Hylättiin viimeisin tekstimuutos (tehnyt <user>) ja palautettiin versio 16331550, jonka on tehnyt <user>

**REMOVED:**
```
'''Suomen sisällissota''' käytiin [[Suomi|Suomen]] [[Suomen valtioneuvosto|senaatin]] eli [[Svinhufvudin I hallitus|hallituksen]] ja sitä vastaan [[kapina|kapinoineen]] [[Suomen kansanvaltuuskunta|Suomen kansanvaltuuskunnan]] johtamien joukkojen välillä [[27.
…
Sisällissota oli osa [[ensimmäinen maailmansota|ensimmäisen maailmansodan]] aiheuttamaa valtiollista ja yhteiskunnallista murrosvaihetta Euroopassa.
```
**INSERTED:**
```
'''Suomen vapaussota''' käytiin [[Suomi|Suomen]] [[Suomen valtioneuvosto|senaatin]] eli [[Svinhufvudin I hallitus|hallituksen]] ja sitä vastaan [[kapina|kapinoineen]] [[Suomen kansanvaltuuskunta|Suomen kansanvaltuuskunnan]] johtamien joukkojen välillä [[27.
…
eli punikit
…
Vapaussota oli osa [[ensimmäinen maailmansota|ensimmäisen maailmansodan]] aiheuttamaa valtiollista ja yhteiskunnallista murrosvaihetta Euroopassa.
…
jonka aiheutti punaisten maanpetoksellinen toiminta,
…
- laillisen hallituksen joukot ja kapinalliset punikkijoukot
```
**CONTEXT (before the edit):**
```
…767 katosi<br>11 652 kuoli vankileireillä<br>10 000–12 000 haavoittui<br><br>'''venäläiset:'''<br>700–900 kaatui<br>1&nbsp;500 teloitettiin
 | huomautus = 
 | muut_taistelut = 
}}
'''Suomen sisällissota''' käytiin [[Suomi|Suomen]] [[Suomen valtioneuvosto|senaatin]] eli [[Svinhufvudin I hallitus|hallituksen]] ja sitä vastaan [[kapina|kapinoineen]] [[Suomen kansanvaltuuskunta|Suomen…
```
**Output A:**
```json
{
 "what_changed_en": "Renamed the Finnish Civil War ('sisällissota') to the 'War of Freedom' ('vapaussota'), added derogatory terms ('punikit') for the Reds, and inserted a claim that the war was caused by the Reds' treasonous actions.",
 "evidence": [
  "'''Suomen vapaussota'''",
  "eli punikit",
  "jonka aiheutti punaisten maanpetoksellinen toiminta,",
  "- laillisen hallituksen joukot ja kapinalliset punikkijoukot"
 ],
 "claim_en": "The 1918 conflict was a war of independence caused by the treasonous actions of the Reds.",
 "claim_fi": "Vuoden 1918 sota oli vapaussota, jonka aiheutti punaisten maanpetoksellinen toiminta.",
 "narrative_en": "White-side framing of the Finnish Civil War",
 "category": "pov_push",
 "confidence": 5,
 "historical_note_en": null,
 "uncertainty_note": null
}
```
**Output B:**
```json
{
 "what_changed_en": "The editor changed instances of 'Suomen sisällissota' (Finnish Civil War) to 'Suomen vapaussota' (Finnish War of Independence), added derogatory terms for the Red side ('punikit'), and inserted text blaming the conflict on the treasonous actions of the Reds.",
 "evidence": [
  "Suomen vapaussota",
  "eli punikit",
  "jonka aiheutti punaisten maanpetoksellinen toiminta"
 ],
 "claim_en": "The 1918 conflict in Finland was a war of independence caused by the treason of the Reds.",
 "claim_fi": "Vuoden 1918 konflikti Suomessa oli vapaussota, jonka aiheutti punaisten maanpetos.",
 "narrative_en": "White/nationalist framing of the Finnish Civil War as a War of Independence",
 "category": "pov_push",
 "confidence": 5,
 "historical_note_en": "The term 'Finnish Civil War' is the neutral and widely accepted historical consensus, whereas 'War of Independence' is a partisan name used historically by the victorious White side.",
 "uncertainty_note": null
}
```
**Output C:**
```json
{
 "what_changed_en": "Replaced 'sisällissota' (civil war) with 'vapaussota' (war of liberation), used the derogatory term 'punikit' (Reds) and claimed the war was caused by treasonous actions of the Reds.",
 "evidence": [
  "'''Suomen vapaussota'''",
  "eli punikit",
  "jonka aiheutti punaisten maanpetoksellinen toiminta,",
  "- laillisen hallituksen joukot ja kapinalliset punikkijoukot"
 ],
 "claim_en": "The Finnish Civil War was a War of Liberation caused by treasonous actions by the rebel Red forces.",
 "claim_fi": "Suomen sisällissota oli vapaussota, jonka aiheutti kapinallisten punaisten maanpetoksellinen toiminta.",
 "narrative_en": "Traditional White interpretation of the Finnish Civil War",
 "category": "pov_push",
 "confidence": 5,
 "historical_note_en": null,
 "uncertainty_note": null
}
```
**Output D:**
```json
{
 "what_changed_en": "Replaced the neutral name 'Finnish Civil War' ('Suomen sisällissota') with the White partisan name 'Finnish War of Liberation' ('Suomen vapaussota'), added the derogatory slur 'punikit' ('Reds'/'commies') to refer to the Red Guards, and inserted claims that the war was caused by the Reds' 'treasonous activity' ('maanpetoksellinen toiminta') against the 'legal government' ('laillisen hallituksen').",
 "evidence": [
  "'''Suomen vapaussota'''",
  "eli punikit",
  "jonka aiheutti punaisten maanpetoksellinen toiminta,",
  "- laillisen hallituksen joukot ja kapinalliset punikkijoukot"
 ],
 "claim_en": "The 1918 conflict in Finland was a War of Liberation caused by the treasonous rebellion of the Reds against the legal government.",
 "claim_fi": "Vuoden 1918 sota Suomessa oli vapaussota, jonka aiheutti punaisten maanpetoksellinen kapina laillista hallitusta vastaan.",
 "narrative_en": "White Finland / 'War of Liberation' partisan historiography",
 "category": "pov_push",
 "confidence": 5,
 "historical_note_en": "Modern Finnish historiography overwhelmingly refers to the 1918 conflict neutrally as 'Suomen sisällissota' (the Finnish Civil War) and explains its outbreak through complex political, social, and international crises rather than solely blaming one side's 'treason'.",
 "uncertainty_note": null
}
```
**Output E:**
```json
{
 "what_changed_en": "Replaced the neutral term \"Finnish Civil War\" (Suomen sisällissota) with \"Finnish War of Liberation\" (Suomen vapaussota), added the derogatory term \"punikit\" (Reddies) for the Reds, and inserted claims blaming the war on the \"treasonous actions of the Reds\" against the \"legitimate government's troops\".",
 "evidence": [
  "'''Suomen vapaussota'''",
  "eli punikit",
  "jonka aiheutti punaisten maanpetoksellinen toiminta,",
  "- laillisen hallituksen joukot ja kapinalliset punikkijoukot"
 ],
 "claim_en": "The 1918 conflict was a War of Liberation caused by the treasonous actions of rebellious \"Reddies\" against the legitimate government.",
 "claim_fi": "Vuoden 1918 sota oli vapaussota, jonka aiheutti kapinallisten punikkien maanpetoksellinen toiminta laillista hallitusta vastaan.",
 "narrative_en": "White interpretation of the 1918 Finnish Civil War (vapaussota narrative)",
 "category": "pov_push",
 "confidence": 5,
 "historical_note_en": null,
 "uncertainty_note": null
}
```

## Edit 11 — id `rev-18422484`

- Article: Jatkosota  
- When: 2019-09-20T07:52:11Z, anonymous editor, live for 1 h 33 min
- Edit summary: "Neuvostoliiton voitto." Suomi ei hävinnyt jatkosotaa, mutta olisi murtunut ilman rauhaa. Ottaen huomioon tappiot kummallakin osapuolella, Neuvostoliitto ei voittanut, vaan Suomi painostettiin rauhaan.  
- Revert summary: Kumottu muokkaus 18422484, jonka teki <user> (keskustelu)

**REMOVED:**
```
| lopputulos     = Neuvostoliiton voitto, [[Moskovan välirauha]] (1944)
```
**INSERTED:**
```
| lopputulos     = Suomi painostettiin rauhaan, [[Moskovan välirauha]] (1944)
```
**CONTEXT (before the edit):**
```
…kommunistien toiminta laillistettiin. Suomi joutui myöhemmässä ulkopolitiikassaan ottamaan tarkkaan huomioon Neuvostoliiton edut ja maiden välille solmittiin [[YYA-sopimus]].
| lopputulos     = Neuvostoliiton voitto, [[Moskovan välirauha]] (1944)
| taistelija1    = {{Suomi}}<br>{{Saksa (1935-1945)}}
| taistelija2    = {{Neuvostoliitto (1923-1955)}}
| komentaja1     = {{Suomen lippu}}…
```
**Output A:**
```json
{
 "what_changed_en": "Replaced the infobox result from 'Soviet victory' to 'Finland was pressured into peace', arguing in the edit summary that Finland did not lose the war.",
 "evidence": [
  "| lopputulos     = Suomi painostettiin rauhaan, [[Moskovan välirauha]] (1944)",
  "\"Neuvostoliiton voitto.\" Suomi ei hävinnyt jatkosotaa",
  "Neuvostoliitto ei voittanut, vaan Suomi painostettiin rauhaan."
 ],
 "claim_en": "The Continuation War did not end in a Soviet victory, but rather Finland was pressured into peace.",
 "claim_fi": "Jatkosota ei päättynyt Neuvostoliiton voittoon, vaan Suomi painostettiin rauhaan.",
 "narrative_en": "Denial or downplaying of Finland's military defeat in the Continuation War",
 "category": "pov_push",
 "confidence": 5,
 "historical_note_en": "Standard Finnish and international historiography regards the Continuation War as an Allied/Soviet strategic victory, resulting in Finland ceding territories, agreeing to reparations, and expelling German troops.",
 "uncertainty_note": null
}
```
**Output B:**
```json
{
 "what_changed_en": "The editor changed the outcome of the Continuation War in the infobox from \"Soviet victory\" to \"Finland was pressured into peace\".",
 "evidence": [
  "| lopputulos     = Neuvostoliiton voitto, [[Moskovan välirauha]] (1944)",
  "| lopputulos     = Suomi painostettiin rauhaan, [[Moskovan välirauha]] (1944)",
  "Neuvostoliiton voitto."
 ],
 "claim_en": "Finland did not lose the Continuation War, but was pressured into peace.",
 "claim_fi": "Suomi ei hävinnyt jatkosotaa, vaan Suomi painostettiin rauhaan.",
 "narrative_en": "Continuation War outcome revisionism",
 "category": "content_dispute",
 "confidence": 4,
 "historical_note_en": null,
 "uncertainty_note": null
}
```
**Output C:**
```json
{
 "what_changed_en": "Changed the outcome of the Continuation War in the infobox from \"Soviet victory\" to \"Finland was pressured into peace\", arguing in the edit summary that Finland did not lose and the Soviet Union did not win.",
 "evidence": [
  "Neuvostoliiton voitto, [[Moskovan välirauha]] (1944)",
  "Suomi painostettiin rauhaan, [[Moskovan välirauha]] (1944)",
  "Suomi ei hävinnyt jatkosotaa, mutta olisi murtunut ilman rauhaa.",
  "Neuvostoliitto ei voittanut, vaan Suomi painostettiin rauhaan."
 ],
 "claim_en": "The Continuation War did not result in a Soviet victory, as Finland did not lose the war but was merely pressured into peace.",
 "claim_fi": "Jatkosota ei päättynyt Neuvostoliiton voittoon, sillä Suomi ei hävinnyt sotaa vaan ainoastaan painostettiin rauhaan.",
 "narrative_en": "Finnish nationalist framing of the Continuation War as a defensive non-defeat (torjuntavoitto)",
 "category": "pov_push",
 "confidence": 5,
 "historical_note_en": "Mainstream historiography classifies the Continuation War as a Soviet victory, as Finland was forced to retreat, cede territory, pay heavy reparations, and accept Soviet-imposed armistice terms.",
 "uncertainty_note": null
}
```
**Output D:**
```json
{
 "what_changed_en": "Changed the outcome of the Continuation War in the infobox from \"Soviet victory\" (\"Neuvostoliiton voitto\") to \"Finland was pressured into peace\" (\"Suomi painostettiin rauhaan\"). In the edit summary, the editor argued that considering the casualties on both sides, the Soviet Union did not win and Finland did not lose.",
 "evidence": [
  "| lopputulos     = Neuvostoliiton voitto, [[Moskovan välirauha]] (1944)",
  "| lopputulos     = Suomi painostettiin rauhaan, [[Moskovan välirauha]] (1944)",
  "Suomi ei hävinnyt jatkosotaa, mutta olisi murtunut ilman rauhaa. Ottaen huomioon tappiot kummallakin osapuolella, Neuvostoliitto ei voittanut, vaan Suomi painostettiin rauhaan."
 ],
 "claim_en": "The Continuation War did not end in a Soviet victory, as Finland did not lose the war but was instead pressured into peace.",
 "claim_fi": "Jatkosota ei päättynyt Neuvostoliiton voittoon eikä Suomi hävinnyt sotaa, vaan Suomi painostettiin rauhaan.",
 "narrative_en": "Finnish nationalist framing of the Continuation War outcome (torjuntavoitto / denial of military defeat)",
 "category": "pov_push",
 "confidence": 5,
 "historical_note_en": "In mainstream historiography, the Continuation War is recognized as ending in a Soviet victory; under the 1944 Moscow Armistice, Finland had to cede territory, pay heavy war reparations, and expel German forces, even though it avoided unconditional surrender and Soviet occupation.",
 "uncertainty_note": null
}
```
**Output E:**
```json
{
 "what_changed_en": "Changed the infobox outcome of the Continuation War from \"Soviet victory\" to \"Finland was pressured into peace\".",
 "evidence": [
  "| lopputulos     = Neuvostoliiton voitto, [[Moskovan välirauha]] (1944)",
  "| lopputulos     = Suomi painostettiin rauhaan, [[Moskovan välirauha]] (1944)",
  "Neuvostoliitto ei voittanut, vaan Suomi painostettiin rauhaan."
 ],
 "claim_en": "The Soviet Union did not win the Continuation War; rather, Finland was pressured into peace.",
 "claim_fi": "Neuvostoliitto ei voittanut jatkosotaa, vaan Suomi painostettiin rauhaan.",
 "narrative_en": "Minimising Finnish defeat in the Continuation War",
 "category": "pov_push",
 "confidence": 5,
 "historical_note_en": "The Continuation War ended in a Soviet strategic victory and the Moscow Armistice of 1944, in which Finland ceded territory and agreed to pay war reparations.",
 "uncertainty_note": null
}
```

## Edit 12 — id `rev-19544342`

- Article: Carl Gustaf Emil Mannerheim  
- When: 2021-02-03T20:32:16Z, registered editor, live for 3 min
- Edit summary: Suomenruotsalaisten kohdalla mainitaan määritelmässä suomenruotsalaisuus, määritelmään asiat joista henkilö tunnetaan parhaiten, sukupuoleen viittaaminen turhaa  
- Revert summary: Kumottu muokkaus 19544342, jonka teki <user> (keskustelu) määritelmälauseessa kerrotaan kansalaisuus, ei etnisyyttä

**REMOVED:**
```
suomalainen [[Aateli|aatelismies]], [[sotilas]] ja [[sitoutumaton]] poliitikko
…
Hän oli itsenäisen Suomen toinen [[Suomen valtionhoitaja|valtionhoitaja]] (1918–1919), kuudes [[Suomen tasavallan presidentti|
…
(1944–1946) ja [[Suomen marsalkka]]
```
**INSERTED:**
```
suomenruotsalainen [[Suomen marsalkka|marsalkka]], [[Aateli|aatelinen]] ja [[Suomen tasavallan presidentti|Suomen kuudes presidentti]]
…
Hän oli itsenäisen Suomen toinen [[Suomen valtionhoitaja|valtionhoitaja]] vuosina 1918–1919 ja kuudes [[Suomen tasavallan presidentti|tasavallan
```
**CONTEXT (before the edit):**
```
…Emil Mannerheim''' ([[4. kesäkuuta]] [[1867]] [[Askainen]], [[Suomen suuriruhtinaskunta]], [[Venäjän keisarikunta]] – [[27. tammikuuta]] [[1951]] [[Lausanne]], [[Sveitsi]])<ref name="klinge"/> oli suomalainen [[Aateli|aatelismies]], [[sotilas]] ja [[sitoutumaton]] poliitikko. Hän oli itsenäisen Suomen toinen [[Suomen valtionhoitaja|valtionhoitaja]] (1918–1919), kuudes [[Suomen tasavallan…
```
**Output A:**
```json
{
 "what_changed_en": "Changed the lead description of Mannerheim from \"Finnish nobleman, soldier and independent politician\" to \"Finland-Swedish marshal, noble and sixth president of Finland\", and adjusted the following sentence accordingly.",
 "evidence": [
  "suomalainen [[Aateli|aatelismies]], [[sotilas]] ja [[sitoutumaton]] poliitikko",
  "suomenruotsalainen [[Suomen marsalkka|marsalkka]], [[Aateli|aatelinen]] ja [[Suomen tasavallan presidentti|Suomen kuudes presidentti]]",
  "Suomenruotsalaisten kohdalla mainitaan määritelmässä suomenruotsalaisuus",
  "määritelmälauseessa kerrotaan kansalaisuus, ei etnisyyttä"
 ],
 "claim_en": "Mannerheim should be defined in the lead sentence as Finland-Swedish rather than Finnish.",
 "claim_fi": "Mannerheim tulisi määritellä johdantolauseessa suomenruotsalaiseksi eikä suomalaiseksi.",
 "narrative_en": null,
 "category": "content_dispute",
 "confidence": 5,
 "historical_note_en": null,
 "uncertainty_note": null
}
```
**Output B:**
```json
{
 "what_changed_en": "Changed the opening definition of Mannerheim from 'Finnish nobleman, soldier and non-aligned politician' to 'Finland-Swedish Marshal, noble and Finland's sixth president'.",
 "evidence": [
  "suomenruotsalainen [[Suomen marsalkka|marsalkka]]",
  "määritelmälauseessa kerrotaan kansalaisuus, ei etnisyyttä",
  "Suomenruotsalaisten kohdalla mainitaan määritelmässä suomenruotsalaisuus"
 ],
 "claim_en": "Carl Gustaf Emil Mannerheim should be defined primarily as Finland-Swedish rather than Finnish in the lead sentence.",
 "claim_fi": "Carl Gustaf Emil Mannerheim tulisi määritellä johdantolauseessa ensisijaisesti suomenruotsalaiseksi suomalaisen sijaan.",
 "narrative_en": null,
 "category": "content_dispute",
 "confidence": 4,
 "historical_note_en": null,
 "uncertainty_note": "Could be viewed as a style guideline violation/good_faith_error regarding Wikipedia lead sentence conventions, but the edit summaries indicate a direct content disagreement over ethnicity vs. nationality."
}
```
**Output C:**
```json
{
 "what_changed_en": "Rewrote the lead definition to describe Mannerheim as a 'Finland-Swedish marshal, noble, and sixth president of Finland' instead of a 'Finnish nobleman, soldier, and independent politician'. The editor argued for highlighting Finland-Swedish identity, gender-neutral terms, and his best-known roles, while the reverter stated the lead should name nationality rather than ethnicity.",
 "evidence": [
  "suomalainen [[Aateli|aatelismies]], [[sotilas]] ja [[sitoutumaton]] poliitikko",
  "suomenruotsalainen [[Suomen marsalkka|marsalkka]], [[Aateli|aatelinen]] ja [[Suomen tasavallan presidentti|Suomen kuudes presidentti]]",
  "Suomenruotsalaisten kohdalla mainitaan määritelmässä suomenruotsalaisuus",
  "määritelmälauseessa kerrotaan kansalaisuus, ei etnisyyttä"
 ],
 "claim_en": "Mannerheim should be defined in the lead sentence as a Finland-Swedish marshal, noble, and sixth president of Finland rather than a Finnish nobleman, soldier, and independent politician.",
 "claim_fi": "Mannerheim tulisi määritellä johdantolauseessa suomenruotsalaiseksi marsalkaksi, aateliseksi ja Suomen kuudenneksi presidentiksi suomalaisen aatelismiehen, sotilaan ja sitoutumattoman poliitikon sijaan.",
 "narrative_en": null,
 "category": "content_dispute",
 "confidence": 4,
 "historical_note_en": null,
 "uncertainty_note": "Could also be classified as good_faith_error since the revert specifically cites Finnish Wikipedia's biographical style convention regarding nationality versus ethnicity/language group in the opening sentence, or mildly pov_push if the editor systematically promotes ethnolinguistic labels."
}
```
**Output D:**
```json
{
 "what_changed_en": "Changed the description of Carl Gustaf Emil Mannerheim in the opening sentence from 'Finnish nobleman, soldier and independent politician' to 'Finland-Swedish marshal, nobleman and sixth president of Finland', removing the word 'suomalainen' (Finnish) and replacing it with 'suomenruotsalainen' (Finland-Swedish).",
 "evidence": [
  "suomenruotsalainen [[Suomen marsalkka|marsalkka]], [[Aateli|aatelinen]] ja [[Suomen tasavallan presidentti|Suomen kuudes presidentti]]",
  "määritelmälauseessa kerrotaan kansalaisuus, ei etnisyyttä"
 ],
 "claim_en": "Carl Gustaf Emil Mannerheim should be defined primarily as Finland-Swedish rather than Finnish in the opening sentence.",
 "claim_fi": "Carl Gustaf Emil Mannerheim tulisi määritelmälauseessa määritellä ensisijaisesti suomenruotsalaiseksi suomalaisen sijaan.",
 "narrative_en": "Emphasis on Mannerheim's Finland-Swedish ethnicity in biographical leads.",
 "category": "content_dispute",
 "confidence": 4,
 "historical_note_en": null,
 "uncertainty_note": "Could be seen as pov_push regarding ethnic categorization, but stems from a direct disagreement over Wikipedia style regarding whether lead sentences should state nationality or ethnicity."
}
```
**Output E:**
```json
{
 "what_changed_en": "Changed the opening definition of Mannerheim from \"Finnish nobleman, soldier, and non-partisan politician\" to \"Finland-Swedish marshal, noble, and the sixth President of Finland\", moving his titles into the first sentence. It was reverted with a summary stating that the lead definition states nationality rather than ethnicity.",
 "evidence": [
  "suomalainen [[Aateli|aatelismies]], [[sotilas]] ja [[sitoutumaton]] poliitikko",
  "suomenruotsalainen [[Suomen marsalkka|marsalkka]], [[Aateli|aatelinen]] ja [[Suomen tasavallan presidentti|Suomen kuudes presidentti]]",
  "Suomenruotsalaisten kohdalla mainitaan määritelmässä suomenruotsalaisuus, määritelmään asiat joista henkilö tunnetaan parhaiten, sukupuoleen viittaaminen turhaa",
  "määritelmälauseessa kerrotaan kansalaisuus, ei etnisyyttä"
 ],
 "claim_en": "Carl Gustaf Emil Mannerheim should be defined in the opening sentence as a Finland-Swedish marshal, noble, and the sixth President of Finland.",
 "claim_fi": "Carl Gustaf Emil Mannerheim oli suomenruotsalainen marsalkka, aatelinen ja Suomen kuudes presidentti.",
 "narrative_en": null,
 "category": "content_dispute",
 "confidence": 4,
 "historical_note_en": null,
 "uncertainty_note": "Could also be classified as good_faith_error since the revert points out a Wikipedia style convention regarding nationality vs. ethnicity/language group in the lead sentence."
}
```

## Edit 13 — id `rev-22741016`

- Article: Metsäsaamelaiset  
- When: 2024-10-31T19:05:00Z, anonymous editor, live for 33 min
- Edit summary: pieniä tarkennuksia  
- Revert summary: Hylättiin viimeisin tekstimuutos (tehnyt <user>) ja palautettiin versio 22686943, jonka on tehnyt <user>: Säilytetään neutraalimpi linja

**REMOVED:**
```
'''Metsäsaamelaiset''' kokevat olevansa yksi [[Suomi|Suomen]] alueella elävistä [[saamelaiset|saamelaisryhmistä]].<ref name=":0">{{Kirjaviite |Tekijä=Antikainen, Janne et al.
…
Metsäsaamelaisilla ei enää ole omaa kieltä eikä heitä ole hyväksytty [[Saamelaiskäräjät (Suomi)|Saamelaiskäräjien]] vaaliluetteloon.<ref>{{Verkkoviite | Osoite = https://www.kho.fi/fi/index/paatokset/vuosikirjapaatokset/1619006053846.html| Nimeke = KHO:2021:48 | Julkaisija = Korkein hallinto-oikeus| Tekijä = | Ajankohta = | Viitattu = }}</ref> Metsäsaamelaiset julistautuivat lokakuun 2021 lopussa yhdeksi Suomen alkuperäisistä saamelaisryhmistä.<ref>{{Verkkoviite | Osoite = https://www.metsasaamelaiset.fi/julistus/ | Nimeke = Suomen alkuperäisten saamelaisten julistus | Julkaisija = Lapinkylien parlamentti | Viitattu = 11.11.2022}}</ref>
…
Keminsaame on kielenä kuollut monista syistä, kuten suomen kielen leviämisestä tai häpeästä johtuen mutta myös entisaikojen opettajien nuivan ja vastustavan suhtautumisen takia
```
**INSERTED:**
```
'''Metsäsaamelaiset''' ovat yksi [[Suomi|Suomen]] alueella elävistä [[saamelaiset|saamelaisryhmistä]].<ref name=":0">{{Kirjaviite |Tekijä=Antikainen, Janne et al.
…
Metsäsaamelaisilla on oma kieli, jota on puhuttu vielä 1900-luvun alkupuolella. Meysaäsaamelaisia ei ole hyväksytty [[Saamelaiskäräjät (Suomi)|Saamelaiskäräjien]] vaaliluetteloon.<ref>{{Verkkoviite | Osoite = https://www.kho.fi/fi/index/paatokset/vuosikirjapaatokset/1619006053846.html| Nimeke = KHO:2021:48 | Julkaisija = Korkein hallinto-oikeus| Tekijä = | Ajankohta = | Viitattu = }}</ref> Metsäsaamelaiset ovat yksi Suomen alkuperäisistä saamelaisryhmistä.<ref>{{Verkkoviite | Osoite = https://www.metsasaamelaiset.fi/julistus/ | Nimeke = Suomen alkuperäisten saamelaisten julistus | Julkaisija = Lapinkylien parlamentti | Viitattu = 11.11.2022}}</ref>
…
jolloin
…
Keminsaamea ei ole enää puhuttu monista syistä, kuten suomen kielen leviämisestä tai häpeästä johtuen mutta myös entisaikojen opettajien nuivan ja vastustavan suhtautumisen takia. Keminsaamea elvytetään
```
**CONTEXT (before the edit):**
```
'''Metsäsaamelaiset''' kokevat olevansa yksi [[Suomi|Suomen]] alueella elävistä [[saamelaiset|saamelaisryhmistä]].<ref name=":0">{{Kirjaviite |Tekijä=Antikainen, Janne et al. |Nimeke=Metsälappalainen kulttuuri ja sen edistäminen |Vuosi=2019 |Julkaisija=Valtioneuvoston kanslia |Isbn=978-952-287-763-5…
```
**Output A:**
```json
{
 "what_changed_en": "Changed attributed statements about Forest Sámi (\"feel that they are\", \"declared themselves to be\" a Sámi group) into direct statements of fact (\"are\" an indigenous Sámi group), and replaced the statement that their language (Kemi Sámi) is extinct with the claim that they have a language that was spoken into the early 20th century and is being revived.",
 "evidence": [
  "'''Metsäsaamelaiset''' kokevat olevansa yksi [[Suomi|Suomen]] alueella elävistä [[saamelaiset|saamelaisryhmistä]].",
  "'''Metsäsaamelaiset''' ovat yksi [[Suomi|Suomen]] alueella elävistä [[saamelaiset|saamelaisryhmistä]].",
  "Metsäsaamelaisilla on oma kieli, jota on puhuttu vielä 1900-luvun alkupuolella.",
  "Säilytetään neutraalimpi linja"
 ],
 "claim_en": "The Forest Sámi are one of the indigenous Sámi groups living in Finland and have their own language (Kemi Sámi) which is being revived.",
 "claim_fi": "Metsäsaamelaiset ovat yksi Suomen alkuperäisistä saamelaisryhmistä ja heillä on oma elvytettävä kielensä.",
 "narrative_en": "Recognition of Forest Sámi as an official indigenous Sámi group",
 "category": "pov_push",
 "confidence": 5,
 "historical_note_en": "Kemi Sámi became extinct in the 19th century with only limited vocabulary recorded, and the self-identified Forest Sámi (metsäsaamelaiset / metsälappalaiset) are not officially recognized as a Sámi group by the Sámi Parliament of Finland or the Supreme Administrative Court.",
 "uncertainty_note": null
}
```
**Output B:**
```json
{
 "what_changed_en": "Changed hedged attributions (\"feel they are\", \"declared themselves\") to direct assertions that Forest Sámi are one of Finland's indigenous Sámi groups, and replaced statements that they no longer have their own language and that Kemi Sámi is dead with claims that they have their own language which is being revitalized.",
 "evidence": [
  "'''Metsäsaamelaiset''' kokevat olevansa yksi",
  "'''Metsäsaamelaiset''' ovat yksi",
  "Metsäsaamelaiset julistautuivat lokakuun 2021 lopussa yhdeksi Suomen alkuperäisistä saamelaisryhmistä.",
  "Säilytetään neutraalimpi linja"
 ],
 "claim_en": "Forest Sámi are an indigenous Sámi group in Finland with their own language that is being revitalized.",
 "claim_fi": "Metsäsaamelaiset ovat yksi Suomen alkuperäisistä saamelaisryhmistä, joilla on oma elvytettävä kieli.",
 "narrative_en": "Recognition of Forest Sámi as an official Sámi group in Finland",
 "category": "pov_push",
 "confidence": 5,
 "historical_note_en": null,
 "uncertainty_note": null
}
```
**Output C:**
```json
{
 "what_changed_en": "Changed the description of Forest Saami from considering themselves a Sami group to outright stating they are one, asserted they have their own language spoken in the 20th century, and removed mention of them declaring themselves an indigenous group in 2021 in favor of stating they are one.",
 "evidence": [
  "Metsäsaamelaiset''' kokevat olevansa yksi",
  "Metsäsaamelaiset''' ovat yksi",
  "Metsäsaamelaisilla on oma kieli, jota on puhuttu vielä 1900-luvun alkupuolella."
 ],
 "claim_en": "The Forest Saami are definitively an indigenous Sami group with their own language, rather than just identifying as such.",
 "claim_fi": "Metsäsaamelaiset ovat yksi Suomen saamelaisryhmistä ja heillä on oma kieli.",
 "narrative_en": "Advocacy for Forest Saami recognition as an indigenous group",
 "category": "pov_push",
 "confidence": 4,
 "historical_note_en": null,
 "uncertainty_note": null
}
```
**Output D:**
```json
{
 "what_changed_en": "Changed attributed statements about Forest Sámi identity into undisputed facts (replacing 'consider themselves to be' and 'declared themselves' with 'are') while keeping existing citations, and altered claims about the Kemi Sámi language to assert that the group has its own language spoken into the early 20th century and currently being revitalized rather than being extinct. The edit was reverted to maintain a more neutral stance.",
 "evidence": [
  "'''Metsäsaamelaiset''' kokevat olevansa yksi [[Suomi|Suomen]] alueella elävistä [[saamelaiset|saamelaisryhmistä]].",
  "'''Metsäsaamelaiset''' ovat yksi [[Suomi|Suomen]] alueella elävistä [[saamelaiset|saamelaisryhmistä]].",
  "Metsäsaamelaisilla on oma kieli, jota on puhuttu vielä 1900-luvun alkupuolella.",
  "Säilytetään neutraalimpi linja"
 ],
 "claim_en": "Forest Sámi are one of Finland's original Sámi groups and have their own language, Kemi Sámi, which was spoken into the early 20th century and is currently being revitalized.",
 "claim_fi": "Metsäsaamelaiset ovat yksi Suomen alkuperäisistä saamelaisryhmistä, ja heillä on oma kielensä keminsaame, jota puhuttiin vielä 1900-luvun alkupuolella ja jota elvytetään.",
 "narrative_en": "Recognition of Forest Sámi as an official indigenous Sámi group in Finland",
 "category": "pov_push",
 "confidence": 5,
 "historical_note_en": "Mainstream linguistics holds that Kemi Sámi became extinct in the 19th century with virtually minimal surviving documentation, and Forest Sámi self-identification is a contemporary movement not recognized by the Sámi Parliament of Finland.",
 "uncertainty_note": null
}
```
**Output E:**
```json
{
 "what_changed_en": "Changed the lead to state as an established fact that Forest Sámi are a Sámi group rather than that they consider themselves to be one, claimed they have their own language rather than lacking one, and replaced an extinction description of Kemi Sámi with language revitalization.",
 "evidence": [
  "'''Metsäsaamelaiset''' ovat yksi [[Suomi|Suomen]] alueella elävistä [[saamelaiset|saamelaisryhmistä]].",
  "Metsäsaamelaisilla on oma kieli, jota on puhuttu vielä 1900-luvun alkupuolella.",
  "Säilytetään neutraalimpi linja"
 ],
 "claim_en": "Forest Sámi are an established indigenous Sámi group in Finland who have their own language.",
 "claim_fi": "Metsäsaamelaiset ovat yksi Suomen alkuperäisistä saamelaisryhmistä, joilla on oma kieli.",
 "narrative_en": "Recognition of Forest Sámi indigenous identity",
 "category": "pov_push",
 "confidence": 4,
 "historical_note_en": null,
 "uncertainty_note": null
}
```

## Edit 14 — id `rev-8960233`

- Article: Lapin sota  
- When: 2010-08-27T10:13:49Z, registered editor, live for 10 h 36 min
- Edit summary: tieto on sinänsä ehkä totta, mutta turha. Sodissa ei ole todellisia voittajia unknow  
- Revert summary: Muissakin ja muun kielisissä sota-artikkeleissa mainitaan voittaja(t), miksi ei sitten myös Suomen osuuksissa toisessa maailmansodassa.

**REMOVED:**
```
| lopputulos = Suomen voitto<br>(saksalaisjoukot vetäytyivät Suomen alueelta)
```
**INSERTED:**
```
| lopputulos = Saksalaisjoukot vetäytyivät Suomen alueelta.
```
**CONTEXT (before the edit):**
```
…| päivämäärä = [[15. syyskuuta]] [[1944]] – [[27. huhtikuuta]] [[1945]]
 | paikka = pääasiassa [[Lapin maakunta|Lappi]]
 | casus = [[Moskovan välirauha]]n ehdot
 | aluemuutokset = 
 | lopputulos = Suomen voitto<br>(saksalaisjoukot vetäytyivät Suomen alueelta)
 | taistelija1 = [[Tiedosto:Flag of Germany 1933.svg|20px]] [[natsi-Saksa|Saksa]]
 | taistelija2 = [[Tiedosto:Flag of Finland.svg|20px]]…
```
**Output A:**
```json
{
 "what_changed_en": "The editor removed 'Finnish victory' from the outcome field of the infobox, leaving only the fact that German troops withdrew from Finnish territory.",
 "evidence": [
  "| lopputulos = Suomen voitto<br>(saksalaisjoukot vetäytyivät Suomen alueelta)",
  "| lopputulos = Saksalaisjoukot vetäytyivät Suomen alueelta.",
  "tieto on sinänsä ehkä totta, mutta turha. Sodissa ei ole todellisia voittajia unknow"
 ],
 "claim_en": "The outcome of the Lapland War should be described merely as the withdrawal of German troops rather than a Finnish victory.",
 "claim_fi": "Lapin sodan lopputuloksena tulisi mainita vain saksalaisjoukkojen vetäytyminen eikä Suomen voittoa.",
 "narrative_en": "Downplaying the designation of military conflicts as having clear winners",
 "category": "content_dispute",
 "confidence": 5,
 "historical_note_en": null,
 "uncertainty_note": null
}
```
**Output B:**
```json
{
 "what_changed_en": "Removed 'Finnish victory' ('Suomen voitto') from the infobox result field of the Lapland War article, leaving only the statement that German forces withdrew from Finnish territory. The editor's summary argued that although perhaps true, calling it a victory is unnecessary because 'there are no real winners in wars'.",
 "evidence": [
  "| lopputulos = Suomen voitto<br>(saksalaisjoukot vetäytyivät Suomen alueelta)",
  "| lopputulos = Saksalaisjoukot vetäytyivät Suomen alueelta.",
  "tieto on sinänsä ehkä totta, mutta turha. Sodissa ei ole todellisia voittajia"
 ],
 "claim_en": "Wars have no real winners, so the result of the Lapland War should be stated solely as the withdrawal of German troops rather than a Finnish victory.",
 "claim_fi": "Sodissa ei ole todellisia voittajia, joten Lapin sodan lopputulokseksi tulisi merkitä vain saksalaisjoukkojen vetäytyminen Suomen voiton sijaan.",
 "narrative_en": "Pacifist or anti-war framing of military outcomes",
 "category": "content_dispute",
 "confidence": 4,
 "historical_note_en": null,
 "uncertainty_note": "Borders on pov_push due to the philosophical anti-war rationale in the edit summary ('there are no real winners in wars'), though infobox result wording for WWII-era Finnish wars is also a recurring editorial dispute on Finnish Wikipedia."
}
```
**Output C:**
```json
{
 "what_changed_en": "Removed 'Finnish victory' from the outcome field of the infobox, leaving only the note that German troops retreated from Finnish territory.",
 "evidence": [
  "| lopputulos = Suomen voitto<br>(saksalaisjoukot vetäytyivät Suomen alueelta)",
  "| lopputulos = Saksalaisjoukot vetäytyivät Suomen alueelta.",
  "tieto on sinänsä ehkä totta, mutta turha. Sodissa ei ole todellisia voittajia unknow"
 ],
 "claim_en": "The outcome of the Lapland War should not be described as a Finnish victory because wars do not have real winners.",
 "claim_fi": "Lapin sodan lopputulosta ei tulisi kuvata Suomen voitoksi, koska sodissa ei ole todellisia voittajia.",
 "narrative_en": null,
 "category": "content_dispute",
 "confidence": 4,
 "historical_note_en": null,
 "uncertainty_note": "Could be considered pov_push (pacifist or editorial stance against labeling war winners), but the edit summaries explicitly frame it as a sincere editorial disagreement over infobox conventions."
}
```
**Output D:**
```json
{
 "what_changed_en": "Removed 'Finnish victory' ('Suomen voitto') from the infobox result of the Lapland War, leaving only the statement that German troops withdrew from Finnish territory.",
 "evidence": [
  "| lopputulos = Suomen voitto<br>(saksalaisjoukot vetäytyivät Suomen alueelta)",
  "| lopputulos = Saksalaisjoukot vetäytyivät Suomen alueelta.",
  "tieto on sinänsä ehkä totta, mutta turha. Sodissa ei ole todellisia voittajia",
  "Muissakin ja muun kielisissä sota-artikkeleissa mainitaan voittaja(t)"
 ],
 "claim_en": "War infoboxes should not name a victor because there are no real winners in wars.",
 "claim_fi": "Sotien tietolaatikoissa ei tulisi mainita voittajaa, koska sodissa ei ole todellisia voittajia.",
 "narrative_en": null,
 "category": "content_dispute",
 "confidence": 5,
 "historical_note_en": null,
 "uncertainty_note": null
}
```
**Output E:**
```json
{
 "what_changed_en": "Removed \"Finnish victory\" (\"Suomen voitto\") from the infobox result field for the Lapland War, leaving only the statement that German troops withdrew from Finnish territory, with the edit summary arguing that wars have no real winners.",
 "evidence": [
  "Suomen voitto",
  "| lopputulos = Saksalaisjoukot vetäytyivät Suomen alueelta.",
  "tieto on sinänsä ehkä totta, mutta turha. Sodissa ei ole todellisia voittajia"
 ],
 "claim_en": "The outcome of the Lapland War should be described solely as the withdrawal of German troops rather than a Finnish victory, because wars have no true winners.",
 "claim_fi": "Lapin sodan lopputulokseksi tulisi merkitä vain saksalaisjoukkojen vetäytyminen eikä Suomen voittoa, koska sodissa ei ole todellisia voittajia.",
 "narrative_en": null,
 "category": "content_dispute",
 "confidence": 4,
 "historical_note_en": null,
 "uncertainty_note": "Could also be seen as pov_push due to the philosophical stance ('wars have no real winners') used to justify removing standard military historiography terminology from the infobox."
}
```

## Edit 15 — id `rev-17089097`

- Article: Suomen sisällissota  
- When: 2018-02-15T13:37:08Z, anonymous editor, live for 22 h 39 min
- Edit summary: /* Jakautunut kansa */  
- Revert summary: Hylättiin viimeisin tekstimuutos (tehnyt <user>) ja palautettiin versio 17068954, jonka on tehnyt <user>: Onko tarpeellinen yksityiskohta?

**REMOVED:**
```
(empty)
```
**INSERTED:**
```
Vuonna 1906 [[Laukon kartano]]n [[torppari]]t ja [[Mäkitupalainen|mäkitupalaiset]] ryhtyivät vaatimaan työolosuhteiden parantamista, mutta kartanon silloinen isäntä vapaaherra [[Herman Standertskjöld-Nordenstam]] ei suostunut tähän. Torpparit alkoivat lakkoilla. Niinpä vapaaherra vei asian oikeuteen. Oikeus tuomitsi lakkoilijat häädettäväksi ja häätöä alettiin toteuttaa 15. tammikuuta 1907.<ref>{{Kirjaviite|Nimeke = Laukon omistajia ja vaiheita|Tekijä=Raevuori, Yrjö|Julkaisija = Laukon kartano|Vuosi=1963|Sivut = 276–287}}</ref> Paikallisten viranomaisten tueksi [[Helsinki|Helsingistä]] kutsuttiin ratsupoliisiosasto. Yhteensä 41 ihmistä häädettiin.<ref name=LaukonHistoria /> Tapaus sai laajaa julkisuutta ja kärjisti yhteiskunnallisen vastakkainasettelun ilmapiiriä, joka huipentui vuoden 1918 sisällissotaan.
```
**CONTEXT (before the edit):**
```
…kesken lopulta yksimielisiä. Tavoite hukkui kuitenkin sisäisten valtakiistojen alle.<ref>Alapuro 1988 ja 1993, Haapala 1993 ja 1995 s. 11–13, 91–148, 152–156, Klinge 1997 s. 483–524</ref>

Sisäistä taistelua vallasta ja yhteiskunnan hajoamista vuoden 1917 aikana heijastelivat monet ilmiöt. Näitä olivat muun muassa haluttomuus ja kyvyttömyys poliittisiin kompromisseihin sekä…
```
**Output A:**
```json
{
 "what_changed_en": "Inserted a sourced paragraph about the 1906–1907 Laukko Manor crofter strike and evictions, stating that the incident escalated social polarization leading up to the 1918 Civil War; reverted as a potentially unnecessary detail.",
 "evidence": [
  "Vuonna 1906 [[Laukon kartano]]n [[torppari]]t ja [[Mäkitupalainen|mäkitupalaiset]] ryhtyivät vaatimaan työolosuhteiden parantamista",
  "Tapaus sai laajaa julkisuutta ja kärjisti yhteiskunnallisen vastakkainasettelun ilmapiiriä, joka huipentui vuoden 1918 sisällissotaan.",
  "Onko tarpeellinen yksityiskohta?"
 ],
 "claim_en": "The 1906–1907 Laukko Manor crofter strike and evictions received wide publicity and escalated the social polarization that culminated in the 1918 Finnish Civil War.",
 "claim_fi": "Vuosien 1906–1907 Laukon kartanon torpparilakko ja häädöt saivat laajaa julkisuutta ja kärjistivät yhteiskunnallista vastakkainasettelua, joka huipentui vuoden 1918 sisällissotaan.",
 "narrative_en": null,
 "category": "content_dispute",
 "confidence": 4,
 "historical_note_en": null,
 "uncertainty_note": "Could also be viewed as a good_faith_error if the paragraph was considered misplaced in a section focused on 1917."
}
```
**Output B:**
```json
{
 "what_changed_en": "Added a paragraph describing the 1906–1907 Laukko manor crofter strike and evictions, stating that the widely publicized case heightened social polarization leading up to the 1918 Civil War. The addition included a book citation and a broken named reference tag (`<ref name=LaukonHistoria />`), and was reverted with a summary questioning whether such detail was necessary in the main Civil War article.",
 "evidence": [
  "Vuonna 1906 [[Laukon kartano]]n [[torppari]]t ja [[Mäkitupalainen|mäkitupalaiset]] ryhtyivät vaatimaan työolosuhteiden parantamista",
  "<ref name=LaukonHistoria />",
  "Tapaus sai laajaa julkisuutta ja kärjisti yhteiskunnallisen vastakkainasettelun ilmapiiriä, joka huipentui vuoden 1918 sisällissotaan.",
  "Onko tarpeellinen yksityiskohta?"
 ],
 "claim_en": "The 1906–1907 crofter strike and mass evictions at Laukko manor received wide publicity and heightened the social polarization that culminated in the 1918 Finnish Civil War.",
 "claim_fi": "Laukon kartanon vuosien 1906–1907 torpparilakko ja joukkohäädöt saivat laajaa julkisuutta ja kärjistivät yhteiskunnallista vastakkainasettelua, joka huipentui vuoden 1918 sisällissotaan.",
 "narrative_en": null,
 "category": "content_dispute",
 "confidence": 4,
 "historical_note_en": null,
 "uncertainty_note": "Could also be classified as good_faith_error because the text appears copied from another article (leaving an undefined named reference `<ref name=LaukonHistoria />`) and placed into a general overview section, though the revert summary specifically frames the rejection as an editorial question of due weight and relevance ('Is this a necessary detail?')."
}
```
**Output C:**
```json
{
 "what_changed_en": "Added a sourced paragraph detailing the 1906–1907 Laukko manor crofter strike and subsequent evictions as background to the social polarization leading to the 1918 Civil War. The reverter removed it as an unnecessary level of detail for the main overview article, and it also included an undefined named reference tag.",
 "evidence": [
  "Vuonna 1906 [[Laukon kartano]]n [[torppari]]t ja [[Mäkitupalainen|mäkitupalaiset]] ryhtyivät vaatimaan työolosuhteiden parantamista",
  "Tapaus sai laajaa julkisuutta ja kärjisti yhteiskunnallisen vastakkainasettelun ilmapiiriä, joka huipentui vuoden 1918 sisällissotaan.",
  "<ref name=LaukonHistoria />",
  "Onko tarpeellinen yksityiskohta?"
 ],
 "claim_en": "The 1906–1907 strike and eviction of crofters at Laukko manor gained wide publicity and exacerbated the social polarization that culminated in the 1918 Finnish Civil War.",
 "claim_fi": "Laukon kartanon torpparien lakko ja häädöt vuosina 1906–1907 saivat laajaa julkisuutta ja kärjistivät yhteiskunnallista vastakkainasettelua, joka huipentui vuoden 1918 sisällissotaan.",
 "narrative_en": null,
 "category": "content_dispute",
 "confidence": 4,
 "historical_note_en": null,
 "uncertainty_note": "Could also be classified as good_faith_error because the text appears copied from another article (indicated by the broken <ref name=LaukonHistoria /> tag) and was reverted for being overly detailed for a broad overview article."
}
```
**Output D:**
```json
{
 "what_changed_en": "Added a referenced paragraph describing the 1906–1907 Laukko tenant farmer evictions as a contributor to the societal polarization that culminated in the 1918 Finnish Civil War.",
 "evidence": [
  "Vuonna 1906 [[Laukon kartano]]n [[torppari]]t ja [[Mäkitupalainen|mäkitupalaiset]] ryhtyivät vaatimaan työolosuhteiden parantamista",
  "Onko tarpeellinen yksityiskohta?"
 ],
 "claim_en": "The 1906–1907 evictions of tenant farmers at Laukko manor intensified social polarization leading up to the 1918 civil war.",
 "claim_fi": "Laukon kartanon torpparihäädöt vuosina 1906–1907 kärjistivät vuoden 1918 sisällissotaan johtanutta yhteiskunnallista vastakkainasettelua.",
 "narrative_en": "Agrarian tensions as causes of the Finnish Civil War",
 "category": "content_dispute",
 "confidence": 4,
 "historical_note_en": null,
 "uncertainty_note": "Could be considered good_faith_error if the addition was deemed excessively detailed for an overview article, but the reverter explicitly questioned relevance ('Onko tarpeellinen yksityiskohta?'), pointing to editorial disagreement on scope."
}
```
**Output E:**
```json
{
 "what_changed_en": "Added a detailed paragraph about the 1907 eviction of tenant farmers at Laukon kartano (Lauko Manor) and its role in escalating social tensions leading up to the 1918 Finnish Civil War, complete with citations.",
 "evidence": [
  "Vuonna 1906 [[Laukon kartano]]n [[torppari]]t ja [[Mäkitupalainen|mäkitupalaiset]] ryhtyivät vaatimaan työolosuhteiden parantamista",
  "Tapaus sai laajaa julkisuutta ja kärjisti yhteiskunnallisen vastakkainasettelun ilmapiiriä, joka huipentui vuoden 1918 sisällissotaan."
 ],
 "claim_en": "The forced eviction of tenant farmers at Lauko Manor in 1907 significantly escalated societal polarization and contributed to the outbreak of the 1918 Civil War.",
 "claim_fi": "Vuoden 1907 Laukon kartanon häätötapaus kärjisti yhteiskunnallista vastakkainasettelua ja myötävaikutti vuoden 1918 sisällissodan puhkeamiseen.",
 "narrative_en": "Roots of the Finnish Civil War in agrarian social conflicts",
 "category": "good_faith_error",
 "confidence": 4,
 "historical_note_en": "The Lauko Manor evictions are a well-documented historical conflict between tenant farmers and estate owners in early 20th-century Finland, though editors disagreed on its weight as a direct precursor in this specific article section.",
 "uncertainty_note": "The revert summary questioned if the detail was necessary ('Onko tarpeellinen yksityiskohta?'), which places this on the borderline between a content dispute over article scope and a good-faith addition."
}
```

## Edit 16 — id `rev-18357147`

- Article: Jääkäripataljoona 27  
- When: 2019-08-21T23:42:06Z, anonymous editor, live for 15 h 4 min
- Edit summary: (empty)  
- Revert summary: rv, eston kiertoa (WPK)

**REMOVED:**
```
[[Kuva:Jaakaripataljooa libaussa.jpg|thumb|400px|Jääkäripataljoona saapuu paraatista Libaussa (nyk. Liepāja) kesällä 1917, joukkoa johtaa ratsailta luutnantti Basse ja häntä seuraa pataljoonan pioneerikomppania]]
…
'''Jääkäripataljoona 27''', virallisesti Kuninkaallinen preussilainen jääkäripataljoona 27
…
vuosina [[1915]]–[[1918]]
…
Osastoa koulutettiin [[Lockstedter Lager]]in leirialueella, nykyisessä Hohenlockstedtissa.
…
[[1915]]
…
(''Königlich Preussisches Jägerbataillon Nr. 27'')
…
Kesä-elokuussa 1916 suomalaiset olivat asemissa Riian edustalla Misse-joella (nyk. [[Misa]]). Pioneerikomppania osallistui [[Schmardenin taistelu]]un (nyk. [[Smārdes]]) [[25. heinäkuuta]] ja kenttähaupitsijaos Ekkau-Kekkaun torjuntataisteluun [[18.
…
Joulukuussa 1916 pataljoona siirrettiin Libauhun (nyk. [[Liepāja]]) rantavartiotehtäviin [[4.
…
Aa-joen talvitaistelut käytiin [[5.
…
Libaussa oltiin pitkä jakso rantavartiossa ja täydennyskoulutuksessa ([[24.
…
== Libau ==
…
joulukuuta 1916 ja saapui junakuljetuksena Libauhun (nyk. [[Liepāja]]) 17.
…
Jääkärien lähes 11 kuukautta kestänyt Libaun-jakso oli toisaalta joukon kehityksen kannalta antoisa sisältäessään monipuolista erikoiskoulutusta, toisaalta tätä jaksoa leimasi etenkin alkupuolella epävarmuus siitä, miten pataljoonan luonne ymmärrettiin ja mikä olisi sen tarkoituksenmukaista käyttöä.
…
Pataljoonan täydennyskoulutus oli Libaussa tuskin ehtinyt alkaa ja toinen "sotajoulu" vietetty, kun tapahtumat kääntyivät yllättävään suuntaan: jo pf…
```
**INSERTED:**
```
[[Kuva:Jaakaripataljooa libaussa.jpg|thumb|400px|Jääkäripataljoona saapuu paraatista Liepājassa (ent. saks. Libau) kesällä 1917, joukkoa johtaa ratsailta luutnantti Basse ja häntä seuraa pataljoonan pioneerikomppania]]
…
'''Jääkäripataljoona 27''', virallisesti ''Preussin kuninkaallinen jääkäripataljoona 27''
…
Osastoa koulutettiin [[Lockstedtin leiri]]llä, nykyisessä Hohenlockstedtissa.
…
Kesä-elokuussa 1916 suomalaiset olivat asemissa Riian edustalla [[asemasota Misajoella|Misajoella]] (ent {{k-de|Misse}}). Pioneerikomppania osallistui [[Smārdesin taistelu|taisteluun Smārdesissa]] (ent. saks. ''Schmarden'') [[25. heinäkuuta]] ja kenttähaupitsijaos torjuntataisteluun Iecavassa ja Ķekavassa (ent. saks. ''Eckau'' ja ''Keckau'') [[18.
…
Joulukuussa 1916 pataljoona siirrettiin [[Liepāja]]an (ent. saks. ''Libau'') rantavartiotehtäviin [[4.
…
Talvitaistelut [[Lielupe]]joella (ent. saks. ''Kurländische Aa'') käytiin [[5.
…
Liepājassa oltiin pitkä jakso rantavartiossa ja täydennyskoulutuksessa ([[24.
…
== Liepāja ==
…
joulukuuta 1916 ja saapui junakuljetuksena Liepājaan (ent. saks. ''Libau'') 17.
…
Jääkärien lähes 11 kuukautta kestänyt Liepājan-jakso oli toisaalta joukon kehityksen kannalta antoisa sisältäessään monipuolista erikoiskoulutusta, toisaalta tätä jaksoa leimasi etenkin alkupuolella epävarmuus siitä, miten pataljoonan luonne ymmärrettiin ja mikä olisi sen tarkoituksenmukaista käyttöä.
…
Pataljoonan täydennyskoulutus oli Liepājassa tuskin ehtinyt alkaa ja toinen "sotajoul…
```
**CONTEXT (before the edit):**
```
[[Kuva:Jaakaripataljooa libaussa.jpg|thumb|400px|Jääkäripataljoona saapuu paraatista Libaussa (nyk. Liepāja) kesällä 1917, joukkoa johtaa ratsailta luutnantti Basse ja häntä seuraa pataljoonan pioneerikomppania]]

'''Jääkäripataljoona 27''', virallisesti Kuninkaallinen preussilainen jääkäripataljoona 27 ({{k-de|Königlich Preussisches Jägerbataillon Nr. 27}}), oli Saksan armeijassa vuosina…
```
**Output A:**
```json
{
 "what_changed_en": "Replaced historical German place names in Latvia (such as Libau, Misse, Schmarden, Ekkau-Kekkau, and Aa) with their modern Latvian equivalents (Liepāja, Misa, Smārde, Iecava, Ķekava, and Lielupe) as the primary names throughout the article, and adjusted some German-to-Finnish translations and wikilinks. The edit was reverted as block evasion by a known sockpuppeteer ('WPK').",
 "evidence": [
  "rv, eston kiertoa (WPK)",
  "Liepājassa (ent. saks. Libau)",
  "[[Smārdesin taistelu|taisteluun Smārdesissa]] (ent. saks. ''Schmarden'')",
  "Talvitaistelut [[Lielupe]]joella (ent. saks. ''Kurländische Aa'')"
 ],
 "claim_en": null,
 "claim_fi": null,
 "narrative_en": null,
 "category": "content_dispute",
 "confidence": 4,
 "historical_note_en": null,
 "uncertainty_note": "The edit makes systematic stylistic and naming changes (preferring modern Latvian endonyms over the historical German exonyms traditional in Finnish Jäger historiography) and was reverted primarily because the editor was evading a block (long-term banned user WPK), so 'other' could also apply."
}
```
**Output B:**
```json
{
 "what_changed_en": "Replaced historical German place names (such as Libau, Misse, Schmarden, and Aa-joki) with modern Latvian names (Liepāja, Misajoki, Smārdes, Lielupejoki) and slightly altered the Finnish translation of the battalion's official name.",
 "evidence": [
  "Libaussa (nyk. Liepāja)",
  "Liepājassa (ent. saks. Libau)",
  "rv, eston kiertoa (WPK)",
  "Kuninkaallinen preussilainen jääkäripataljoona 27"
 ],
 "claim_en": null,
 "claim_fi": null,
 "narrative_en": null,
 "category": "content_dispute",
 "confidence": 4,
 "historical_note_en": null,
 "uncertainty_note": "The edit was reverted for block evasion ('eston kiertoa'), so it could also be classified as 'other' depending on whether the focus is on the naming disagreement or the ban evasion."
}
```
**Output C:**
```json
{
 "what_changed_en": "Replaced historical German place names (such as Libau, Schmarden, Misse, and Aa-joki) with modern Latvian names (Liepāja, Smārdes, Misajoki, Lielupe) throughout the article, while noting the former German names.",
 "evidence": [
  "Jääkäripataljoona saapuu paraatista Liepājassa (ent. saks. Libau)",
  "Pioneerikomppania osallistui [[Smārdesin taistelu|taisteluun Smārdesissa]] (ent. saks. ''Schmarden'')",
  "rv, eston kiertoa (WPK)"
 ],
 "claim_en": "Modern Latvian place names should be used as the primary designations instead of historical German names when describing the actions of Jaeger Battalion 27.",
 "claim_fi": "Jääkäripataljoona 27:n toiminnasta kerrottaessa tulisi käyttää ensisijaisesti nykyisiä latvialaisia paikannimiä historiallisten saksankielisten nimien sijaan.",
 "narrative_en": null,
 "category": "good_faith_error",
 "confidence": 4,
 "historical_note_en": null,
 "uncertainty_note": "The revert was made purely due to block evasion ('eston kiertoa (WPK)'), but the edit itself appears to be a good-faith stylistic preference regarding modern vs. historical toponyms."
}
```
**Output D:**
```json
{
 "what_changed_en": "Replaced historical German place names used in traditional Finnish Jäger historiography (such as Libau, Misse, Schmarden, Ekkau-Kekkau, and Aa-joki) with modern Latvian equivalents (Liepāja, Misa, Smārdes, Iecava/Ķekava, Lielupe), adjusted the Finnish translation of the battalion's official name, and unlinked years. The edit was reverted as block evasion by the long-term banned user WPK.",
 "evidence": [
  "rv, eston kiertoa (WPK)",
  "Libaussa (nyk. Liepāja)",
  "Liepājassa (ent. saks. Libau)",
  "[[Smārdesin taistelu|taisteluun Smārdesissa]] (ent. saks. ''Schmarden'')"
 ],
 "claim_en": null,
 "claim_fi": null,
 "narrative_en": null,
 "category": "content_dispute",
 "confidence": 4,
 "historical_note_en": null,
 "uncertainty_note": "The edit was reverted specifically for block evasion by a well-known banned user (WPK) notorious for unilateral naming-convention crusades, so 'other' (policy enforcement) or 'good_faith_error' could also be argued alongside a stylistic/terminological content dispute."
}
```
**Output E:**
```json
{
 "what_changed_en": "The editor updated placenames and historical terms related to Latvian locations (such as Libau to Liepāja) throughout the article, reflecting modern Latvian spelling conventions and adding former German names.",
 "evidence": [
  "Liepājassa (ent. saks. Libau)",
  "Preussin kuninkaallinen jääkäripataljoona 27",
  "[[Liepāja]]an (ent. saks. ''Libau'')"
 ],
 "claim_en": null,
 "claim_fi": null,
 "narrative_en": null,
 "category": "good_faith_error",
 "confidence": 3,
 "historical_note_en": null,
 "uncertainty_note": "The revert summary mentions evading a block (eston kiertoa), suggesting the user might be a known disruptive editor, but the edit itself consists of terminology and styling updates."
}
```

## Edit 17 — id `rev-15356378`

- Article: Mainilan laukaukset  
- When: 2015-11-25T06:02:35Z, registered editor, live for 7 min
- Edit summary: Hylättiin viimeisin tekstimuutos (tehnyt <user>) ja palautettiin versio 15356370, jonka on tehnyt <user>: ylen lähteessä asiaa ei mainita, venäjänkielistä en ymmärrä  
- Revert summary: YLE:n lähde on spekulaatiota, eikä perustu tosiasioihin, ja on ristiriidassa kaikkien muiden lähteiden kanssa. Siksi sitä ei oikeastaan voi käyttää lähteenä.

**REMOVED:**
```
Venäläiset eivät ole myöntäneet, että <ref>{{Verkkoviite | Osoite = http://yle.fi/uutiset/mainilan_laukauksista_70_vuotta/5935602 | Nimeke = Mainilan laukauksista 70 vuotta | Tekijä = | Tiedostomuoto = | Selite = | Julkaisu = Yle Uutiset | Ajankohta = | Julkaisupaikka = | Julkaisija = | Viitattu = 10.6.2014 | Kieli = }}</ref>{{lähde tarkemmin|Asia ei selviä lähteestä. 17.4.2015}} kyseessä olisi ollut [[Lavastettu hyökkäys|Neuvostoliiton itse lavastama tykistöisku]] Mainilan kylään, jonka tarkoituksena oli saada Suomi näyttämään hyökkääjältä talvisodassa. Tämä historiallinen fakta on edelleen kiistanalainen <ref name=":0" />
…
name=":0"
```
**INSERTED:**
```
Venäläiset ovat myöhemmin myöntäneet<ref>{{Verkkoviite | Osoite = http://yle.fi/uutiset/mainilan_laukauksista_70_vuotta/5935602 | Nimeke = Mainilan laukauksista 70 vuotta | Tekijä = | Tiedostomuoto = | Selite = | Julkaisu = Yle Uutiset | Ajankohta = | Julkaisupaikka = | Julkaisija = | Viitattu = 10.6.2014 | Kieli = }}</ref>{{lähde tarkemmin|Asia ei selviä lähteestä. 17.4.2015}} kyseessä olleen [[Lavastettu hyökkäys|Neuvostoliiton itse lavastama tykistöisku]] Mainilan kylään, jonka tarkoituksena oli saada Suomi näyttämään hyökkääjältä talvisodassa
```
**CONTEXT (before the edit):**
```
…| Osoite = http://yle.fi/elavaarkisto/?s=s&g=1&ag=3&t=19&a=8001 | Nimeke = YLE Elävä arkisto: Mainilan laukaukset | Julkaisija = Yleisradio | Viitattu = 2.12.2011}}</ref>

Venäläiset eivät ole myöntäneet, että <ref>{{Verkkoviite | Osoite = http://yle.fi/uutiset/mainilan_laukauksista_70_vuotta/5935602 | Nimeke = Mainilan laukauksista 70 vuotta | Tekijä = | Tiedostomuoto = | Selite…
```
**Output A:**
```json
{
 "what_changed_en": "The editor reverted a previous change, restoring the statement that Russians have later admitted the Shelling of Mainila was a Soviet false-flag attack, and removing the claim that Russians have not admitted it and that the historical fact remains disputed.",
 "evidence": [
  "Venäläiset eivät ole myöntäneet, että",
  "Tämä historiallinen fakta on edelleen kiistanalainen",
  "Venäläiset ovat myöhemmin myöntäneet",
  "ylen lähteessä asiaa ei mainita, venäjänkielistä en ymmärrä"
 ],
 "claim_en": "Russians have later admitted that the Shelling of Mainila was an artillery attack staged by the Soviet Union itself to make Finland appear as the aggressor in the Winter War.",
 "claim_fi": "Venäläiset ovat myöhemmin myöntäneet Mainilan laukausten olleen Neuvostoliiton itse lavastama tykistöisku, jonka tarkoituksena oli saada Suomi näyttämään hyökkääjältä talvisodassa.",
 "narrative_en": null,
 "category": "content_dispute",
 "confidence": 4,
 "historical_note_en": "Historians generally agree that the Shelling of Mainila was a Soviet false-flag operation, which was also acknowledged by Soviet/Russian leaders such as Nikita Khrushchev and Boris Yeltsin.",
 "uncertainty_note": "This edit is itself a revert of another user's change during an edit war over how to phrase and source Russian admissions regarding the Shelling of Mainila."
}
```
**Output B:**
```json
{
 "what_changed_en": "Reverted a previous edit to restore the statement that Russians have later admitted the Shelling of Mainila was staged by the Soviet Union, removing the claim that Russians have not admitted it and that the fact is still disputed.",
 "evidence": [
  "Venäläiset eivät ole myöntäneet, että",
  "Venäläiset ovat myöhemmin myöntäneet",
  "ylen lähteessä asiaa ei mainita, venäjänkielistä en ymmärrä",
  "YLE:n lähde on spekulaatiota, eikä perustu tosiasioihin"
 ],
 "claim_en": "Russians have later admitted that the Shelling of Mainila was an artillery strike staged by the Soviet Union.",
 "claim_fi": "Venäläiset ovat myöhemmin myöntäneet, että Mainilan laukaukset oli Neuvostoliiton itse lavastama tykistöisku.",
 "narrative_en": null,
 "category": "content_dispute",
 "confidence": 4,
 "historical_note_en": null,
 "uncertainty_note": "The edit is a revert of a previous editor's change due to language barrier and source disagreement, so it reflects an ongoing sourcing dispute."
}
```
**Output C:**
```json
{
 "what_changed_en": "Reverted an edit by changing the text back from stating Russians have not admitted staging the Shelling of Mainila to stating that they later did admit it, disputing whether the provided sources support the former claim.",
 "evidence": [
  "Venäläiset eivät ole myöntäneet, että",
  "Venäläiset ovat myöhemmin myöntäneet",
  "ylen lähteessä asiaa ei mainita, venäjänkielistä en ymmärrä",
  "YLE:n lähde on spekulaatiota, eikä perustu tosiasioihin"
 ],
 "claim_en": "Russians have later admitted that the Shelling of Mainila was an artillery strike staged by the Soviet Union.",
 "claim_fi": "Venäläiset ovat myöhemmin myöntäneet kyseessä olleen Neuvostoliiton itse lavastama tykistöisku.",
 "narrative_en": null,
 "category": "content_dispute",
 "confidence": 4,
 "historical_note_en": null,
 "uncertainty_note": "Both editors debate whether Russia/the Soviet Union has formally admitted the shelling was a false flag attack and which sources reliably document this, making it a content dispute rather than clear POV pushing."
}
```
**Output D:**
```json
{
 "what_changed_en": "The editor changed a sentence from stating that Russians have not admitted the shelling of Mainila was a staged Soviet attack to claiming that they have later admitted it, citing a Yle news article.",
 "evidence": [
  "Venäläiset eivät ole myöntäneet",
  "Venäläiset ovat myöhemmin myöntäneet"
 ],
 "claim_en": "Russia has later admitted that the shelling of Mainila was a staged Soviet attack.",
 "claim_fi": "Venäläiset ovat myöhemmin myöntäneet kyseessä olleen Neuvostoliiton itse lavastama tykistöisku Mainilan kylään.",
 "narrative_en": "Russian acknowledgment of historical Soviet aggression.",
 "category": "content_dispute",
 "confidence": 4,
 "historical_note_en": "While Russian leadership (such as Boris Yeltsin) has condemned the Winter War and Soviet actions in general, whether official state admissions specifically regarding the Mainila shelling exist in the way claimed is a subject of interpretation and dispute.",
 "uncertainty_note": null
}
```
**Output E:**
```json
{
 "what_changed_en": "The editor reverted a previous change to restore the statement that Russians have later admitted the Shelling of Mainila was staged by the Soviet Union, removing the claim that Russians have not admitted it and that the historical fact remains disputed. In the edit summary, the editor noted that the Yle source did not support the removed version and that they could not read the Russian-language source.",
 "evidence": [
  "Venäläiset eivät ole myöntäneet, että",
  "Tämä historiallinen fakta on edelleen kiistanalainen",
  "Venäläiset ovat myöhemmin myöntäneet",
  "ylen lähteessä asiaa ei mainita, venäjänkielistä en ymmärrä"
 ],
 "claim_en": "Russians have subsequently admitted that the Shelling of Mainila was a false-flag artillery attack staged by the Soviet Union.",
 "claim_fi": "Venäläiset ovat myöhemmin myöntäneet Mainilan laukausten olleen Neuvostoliiton itse lavastama tykistöisku.",
 "narrative_en": null,
 "category": "content_dispute",
 "confidence": 4,
 "historical_note_en": null,
 "uncertainty_note": "This edit is itself a revert by an established user undoing an earlier change (which appeared to push a revisionist claim denying Russian admission), and was then reverted back in an edit-war exchange arguing over the reliability of the Yle source and how to characterise official versus historiographical Russian acknowledgment."
}
```

## Edit 18 — id `rev-23898635`

- Article: Sumiainen  
- When: 2026-03-17T23:27:55Z, temporary editor, live for 6 h 38 min
- Edit summary: /* Kulttuuria */  
- Revert summary: Hylättiin viimeisin tekstimuutos (tehnyt <user>) ja palautettiin versio 23714070, jonka on tehnyt <user>: ei merkittävää

**REMOVED:**
```
(empty)
```
**INSERTED:**
```
Nimettömien, mutta luotettavien lähteiden mukaan, kylässä on harrastettu pontikankeittoa.<ref>just trust me bro</ref>
```
**CONTEXT (before the edit):**
```
…| Tekijä=Kolmonen, Jaakko (toim.) | Nimeke=Kotomaamme ruoka-aitta: Suomen, Karjalan ja Petsamon pitäjäruoat | Sivu = 95 | Julkaisupaikka=Helsinki | Julkaisija=Patakolmonen | Vuosi=1988 | Tunniste = | Isbn = 951-96047-3-1}}</ref>

==Lähteet==
{{Viitteet}}

== Aiheesta muualla ==
{{Commonscat-rivi}}

{{Keski-Suomi}}
{{Auktoriteettitunnisteet}}

[[Luokka:Sumiainen|*]]
```
**Output A:**
```json
{
 "what_changed_en": "Added a joke sentence stating that according to anonymous but reliable sources, moonshining was practiced in the village, citing \"just trust me bro\".",
 "evidence": [
  "Nimettömien, mutta luotettavien lähteiden mukaan, kylässä on harrastettu pontikankeittoa.",
  "<ref>just trust me bro</ref>"
 ],
 "claim_en": null,
 "claim_fi": null,
 "narrative_en": null,
 "category": "vandalism",
 "confidence": 5,
 "historical_note_en": null,
 "uncertainty_note": null
}
```
**Output B:**
```json
{
 "what_changed_en": "Added a joke sentence claiming that according to anonymous but reliable sources, moonshine brewing has been practiced in the village, cited with a fake reference reading \"just trust me bro\".",
 "evidence": [
  "Nimettömien, mutta luotettavien lähteiden mukaan, kylässä on harrastettu pontikankeittoa.",
  "<ref>just trust me bro</ref>"
 ],
 "claim_en": "Moonshine brewing has been practiced in the village of Sumiainen.",
 "claim_fi": "Sumiaisten kylässä on harrastettu pontikankeittoa.",
 "narrative_en": null,
 "category": "vandalism",
 "confidence": 5,
 "historical_note_en": null,
 "uncertainty_note": null
}
```
**Output C:**
```json
{
 "what_changed_en": "Inserted a joking claim that moonshining has been practiced in the village according to anonymous but reliable sources, with the fake citation 'just trust me bro'.",
 "evidence": [
  "Nimettömien, mutta luotettavien lähteiden mukaan, kylässä on harrastettu pontikankeittoa.",
  "<ref>just trust me bro</ref>"
 ],
 "claim_en": "Moonshining has been practiced in the village of Sumiainen.",
 "claim_fi": "Sumiaisten kylässä on harrastettu pontikankeittoa.",
 "narrative_en": null,
 "category": "vandalism",
 "confidence": 5,
 "historical_note_en": null,
 "uncertainty_note": null
}
```
**Output D:**
```json
{
 "what_changed_en": "Added a claim about moonshine distilling in the village based on anonymous sources with a joke citation.",
 "evidence": [
  "Nimettömien, mutta luotettavien lähteiden mukaan, kylässä on harrastettu pontikankeittoa.",
  "just trust me bro"
 ],
 "claim_en": "Moonshine distilling has been practiced in the village according to anonymous reliable sources.",
 "claim_fi": "Kylässä on harrastettu pontikankeittoa nimettömien, mutta luotettavien lähteiden mukaan.",
 "narrative_en": null,
 "category": "vandalism",
 "confidence": 5,
 "historical_note_en": null,
 "uncertainty_note": null
}
```
**Output E:**
```json
{
 "what_changed_en": "Inserted a joke sentence into the culture section claiming that moonshine brewing has taken place in the village according to 'anonymous, yet reliable sources', accompanied by a fake citation saying 'just trust me bro'.",
 "evidence": [
  "Nimettömien, mutta luotettavien lähteiden mukaan, kylässä on harrastettu pontikankeittoa.",
  "<ref>just trust me bro</ref>",
  "ei merkittävää"
 ],
 "claim_en": "Moonshine brewing has been practiced in Sumiainen.",
 "claim_fi": "Sumiaisissa on harrastettu pontikankeittoa.",
 "narrative_en": null,
 "category": "vandalism",
 "confidence": 5,
 "historical_note_en": null,
 "uncertainty_note": null
}
```

## Edit 19 — id `rev-1412385`

- Article: Suojeluskunta  
- When: 2006-07-24T23:19:14Z, anonymous editor, live for 8 h 5 min
- Edit summary: /* Suojeluskuntien yhteiskunnallinen luonne */  
- Revert summary: palautettu ed. versioon. Luokkapettureista puhuminen on erittäin POV

**REMOVED:**
```
myös
```
**INSERTED:**
```
. Suojeluskuntiin kuului tuhansia "luokkapettureita", jotka tukivat oikestolaista Suomea
```
**CONTEXT (before the edit):**
```
…punaisten puolella olleet työläiset pitivät suojeluskuntiin liittymistä luokkapetturuutena. Kun jotkut työnantajat kuitenkin edellyttivät suojeluskuntien jäsenyyttä, kuului suojeluskuntiin myös jonkun verran työväestöä. Tilanne muuttui hieman Neuvostoliiton hyökättyä Suomeen 1939. Kansallisen yhtenäisyyden nimissä [[Suomen sosialidemokraattinen puolue|SDP]]:n puheenjohtaja [[Väinö…
```
**Output A:**
```json
{
 "what_changed_en": "Inserted a sentence claiming that thousands of 'class traitors' who supported a right-wing Finland belonged to the White Guard (Suojeluskunta).",
 "evidence": [
  "Suojeluskuntiin kuului tuhansia \"luokkapettureita\", jotka tukivat oikestolaista Suomea",
  "Luokkapettureista puhuminen on erittäin POV"
 ],
 "claim_en": "Thousands of 'class traitors' who supported right-wing Finland were members of the Civil Guard.",
 "claim_fi": "Suojeluskuntiin kuului tuhansia \"luokkapettureita\", jotka tukivat oikeistolaista Suomea.",
 "narrative_en": "Left-wing condemnation of working-class Civil Guard members",
 "category": "pov_push",
 "confidence": 5,
 "historical_note_en": null,
 "uncertainty_note": null
}
```
**Output B:**
```json
{
 "what_changed_en": "Replaced the word \"myös\" (\"also\") with a sentence fragment stating that the Civil Guards included thousands of \"class traitors\" who supported a right-wing Finland.",
 "evidence": [
  ". Suojeluskuntiin kuului tuhansia \"luokkapettureita\", jotka tukivat oikestolaista Suomea",
  "Luokkapettureista puhuminen on erittäin POV"
 ],
 "claim_en": "The Civil Guards included thousands of \"class traitors\" who supported a right-wing Finland.",
 "claim_fi": "Suojeluskuntiin kuului tuhansia \"luokkapettureita\", jotka tukivat oikeistolaista Suomea.",
 "narrative_en": "Left-wing framing of working-class Civil Guard members as class traitors",
 "category": "pov_push",
 "confidence": 5,
 "historical_note_en": null,
 "uncertainty_note": null
}
```
**Output C:**
```json
{
 "what_changed_en": "The edit added a loaded characterization describing members of the working class who joined the Civil Guard as 'class traitors'.",
 "evidence": [
  ". Suojeluskuntiin kuului tuhansia \"luokkapettureita\", jotka tukivat oikestolaista Suomea",
  "Luokkapettureista puhuminen on erittäin POV"
 ],
 "claim_en": "Workers who joined the Civil Guard were class traitors supporting right-wing Finland.",
 "claim_fi": "Suojeluskuntiin kuului tuhansia \"luokkapettureita\", jotka tukivat oikeistolaista Suomea.",
 "narrative_en": "Left-wing condemnation of workers joining the Civil Guard",
 "category": "pov_push",
 "confidence": 5,
 "historical_note_en": null,
 "uncertainty_note": null
}
```
**Output D:**
```json
{
 "what_changed_en": "Inserted a sentence claiming that thousands of \"class traitors\" who supported a right-wing Finland belonged to the White Guard (Suojeluskunta), and broke the surrounding sentence in the process.",
 "evidence": [
  ". Suojeluskuntiin kuului tuhansia \"luokkapettureita\", jotka tukivat oikestolaista Suomea",
  "Luokkapettureista puhuminen on erittäin POV"
 ],
 "claim_en": "The White Guard included thousands of working-class \"class traitors\" who supported a right-wing Finland.",
 "claim_fi": "Suojeluskuntiin kuului tuhansia työväenluokkaisia \"luokkapettureita\", jotka tukivat oikeistolaista Suomea.",
 "narrative_en": "Far-left / Red framing of working-class White Guard members as class traitors",
 "category": "pov_push",
 "confidence": 5,
 "historical_note_en": null,
 "uncertainty_note": null
}
```
**Output E:**
```json
{
 "what_changed_en": "Inserted a sentence into the section on the social nature of the White Guard (Suojeluskunta) stating that it included thousands of 'class traitors' ('luokkapettureita') who supported a right-wing Finland, breaking the surrounding sentence structure in the process. The reverter removed it as strongly non-neutral ('POV').",
 "evidence": [
  ". Suojeluskuntiin kuului tuhansia \"luokkapettureita\", jotka tukivat oikestolaista Suomea",
  "Luokkapettureista puhuminen on erittäin POV"
 ],
 "claim_en": "Thousands of working-class members of the White Guard were class traitors who supported a right-wing Finland.",
 "claim_fi": "Tuhannet suojeluskuntiin kuuluneet työläiset olivat luokkapettureita, jotka tukivat oikeistolaista Suomea.",
 "narrative_en": "Far-left condemnation of working-class White Guard members",
 "category": "pov_push",
 "confidence": 5,
 "historical_note_en": null,
 "uncertainty_note": null
}
```

## Edit 20 — id `rev-23817012`

- Article: Suur-Suomi  
- When: 2026-01-30T15:25:34Z, registered editor, live for 23 min
- Edit summary: (empty)  
- Revert summary: Kumottu muokkaus [[Special:Diff/23817012|23817012]], jonka teki <user> (keskustelu) Lähteetöntä

**REMOVED:**
```
(empty)
```
**INSERTED:**
```
Syy siihen, miksi Suomesta ei saatu laajaa apua johtuu osittain Suomen sisäisestä fragmentoitumisesta Valkoisen Terrorin jälkeen.
```
**CONTEXT (before the edit):**
```
…nimeke=SUUR-SUOMEN LOPPU| ajankohta=4.6.2021| selite=SK22/2021, sivut 40-45| julkaisu=Suomen Kuvalehti}}</ref>

Suur-Suomen tavoittelu loppui [[Jatkosota|jatkosodan]] aikana, kun [[Carl Gustaf Emil Mannerheim|ylipäällikkö Mannerheim]] joutui kesäkuussa 1944 luopumaan suomalaisten miehittämästä…
```
**Output A:**
```json
{
 "what_changed_en": "Added an unsourced sentence claiming that the lack of broad support from Finland was partly due to internal fragmentation following the White Terror.",
 "evidence": [
  "Syy siihen, miksi Suomesta ei saatu laajaa apua johtuu osittain Suomen sisäisestä fragmentoitumisesta Valkoisen Terrorin jälkeen.",
  "Lähteetöntä"
 ],
 "claim_en": "The lack of broad support from Finland was partly caused by internal fragmentation after the White Terror.",
 "claim_fi": "Laajan avun puuttuminen Suomesta johtui osittain sisäisestä fragmentoitumisesta valkoisen terrorin jälkeen.",
 "narrative_en": "impact of the White Terror",
 "category": "pov_push",
 "confidence": 3,
 "historical_note_en": null,
 "uncertainty_note": "Could be a good-faith unsourced addition rather than intentional POV pushing."
}
```
**Output B:**
```json
{
 "what_changed_en": "Added an unsourced claim stating that lack of broad support for Greater Finland was partly due to internal fragmentation following the White Terror.",
 "evidence": [
  "Syy siihen, miksi Suomesta ei saatu laajaa apua johtuu osittain Suomen sisäisestä fragmentoitumisesta Valkoisen Terrorin jälkeen."
 ],
 "claim_en": "The lack of broad support for Greater Finland was partly caused by Finland's internal fragmentation after the White Terror.",
 "claim_fi": "Syy siihen, miksi Suomesta ei saatu laajaa apua johtuu osittain Suomen sisäisestä fragmentoitumisesta Valkoisen Terrorin jälkeen.",
 "narrative_en": "internal political division following the Civil War",
 "category": "good_faith_error",
 "confidence": 4,
 "historical_note_en": null,
 "uncertainty_note": "Could be a pov_push depending on intent, but classified as good_faith_error due to lack of sources and plausible historical framing."
}
```
**Output C:**
```json
{
 "what_changed_en": "Inserted an unsourced explanatory sentence claiming that widespread aid from Finland was hindered partly due to internal fragmentation following the White Terror.",
 "evidence": [
  "Syy siihen, miksi Suomesta ei saatu laajaa apua johtuu osittain Suomen sisäisestä fragmentoitumisesta Valkoisen Terrorin jälkeen.",
  "Lähteetöntä"
 ],
 "claim_en": "The lack of widespread aid from Finland was partly due to internal fragmentation following the White Terror.",
 "claim_fi": "Syy siihen, miksi Suomesta ei saatu laajaa apua, johtui osittain Suomen sisäisestä fragmentoitumisesta valkoisen terrorin jälkeen.",
 "narrative_en": "Impact of the White Terror on Finnish society and foreign policy",
 "category": "good_faith_error",
 "confidence": 4,
 "historical_note_en": null,
 "uncertainty_note": "Could be considered pov_push depending on the context of the section (e.g. regarding the Heimosodat), but the revert reason simply cited lack of sources ('Lähteetöntä')."
}
```
**Output D:**
```json
{
 "what_changed_en": "Inserted an unsourced sentence stating that the lack of broad aid from Finland (presumably for the Heimosodat/kinship wars) was partly due to Finland's internal fragmentation following the White Terror. The edit was reverted for being unsourced (\"Lähteetöntä\").",
 "evidence": [
  "Syy siihen, miksi Suomesta ei saatu laajaa apua johtuu osittain Suomen sisäisestä fragmentoitumisesta Valkoisen Terrorin jälkeen.",
  "Lähteetöntä"
 ],
 "claim_en": "Broad aid from Finland for Greater Finland expeditions failed to materialize partly because of internal fragmentation in Finland following the White Terror.",
 "claim_fi": "Laajan avun puuttuminen Suomesta johtui osittain Suomen sisäisestä jakautumisesta valkoisen terrorin jälkeen.",
 "narrative_en": "Emphasising the impact of the 1918 White Terror",
 "category": "good_faith_error",
 "confidence": 3,
 "historical_note_en": null,
 "uncertainty_note": "The edit borders on pov_push by singling out the White Terror rather than the broader 1918 Civil War, and it also appears to be slightly misplaced in the article text (inserted right before a paragraph about the Continuation War). However, the claim that post-1918 political division hindered official and broad popular support for the kinship wars is historically plausible, making an unsourced good-faith addition the most fitting classification."
}
```
**Output E:**
```json
{
 "what_changed_en": "Inserted an unsourced sentence claiming that the lack of broad aid from Finland (presumably during the Kinship Wars) was partly due to internal fragmentation following the White Terror. The edit was reverted for lacking a source (\"Lähteetöntä\").",
 "evidence": [
  "Syy siihen, miksi Suomesta ei saatu laajaa apua johtuu osittain Suomen sisäisestä fragmentoitumisesta Valkoisen Terrorin jälkeen.",
  "Lähteetöntä"
 ],
 "claim_en": "Finland did not provide broad aid partly because of internal fragmentation in Finland following the White Terror.",
 "claim_fi": "Suomesta ei saatu laajaa apua osittain Suomen sisäisen fragmentoitumisen vuoksi valkoisen terrorin jälkeen.",
 "narrative_en": "Emphasising the impact of the White Terror",
 "category": "good_faith_error",
 "confidence": 3,
 "historical_note_en": null,
 "uncertainty_note": "While reverted simply as unsourced and containing anglicised phrasing ('fragmentoitumisesta', capitalised 'Valkoisen Terrorin'), singling out the White Terror rather than the 1918 Civil War more broadly could also reflect subtle POV pushing."
}
```


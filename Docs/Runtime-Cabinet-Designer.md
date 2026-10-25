# Futó alkalmazás: Szekrény készítő

A főmenüben kattints a **Szekrény készítő** gombra. A szerkesztő a Windows standalone buildben és Unity Play módban is fut; nem igényli a Unity Editor menüjét.

Bal oldalt szerkeszthető a szekrény neve, nevesített méretei, színei, anyagai, alkatrészlistája, a kijelölt bútorlap helyzete/forgatása/vastagsága, sokszögű kész körvonala, képletei és élfóliái. Jobb oldalt külön 3D alapanyag- és élfóliatestek látszanak. A bútorlapra kattintva kijelölhető. A Move, Resize, Rotate módokban az X/Y/Z fogantyú húzható. Jobb egérrel kamera forgatható, görgővel zoomolható.

Mentés helye: `Application.persistentDataPath/XML/CabinetDesigns/<id>.xml`. A mentett szekrények a **Megnyitás** listában választhatók, duplikálhatók és törölhetők.

A kész külső körvonal állandó; a nyers szabásgeometria az élfóliázott élek belső eltolásával számítódik. A fóliák saját 3D hálót kapnak. A programban megadott nevesített méretek a körvonalpontok képleteiben (például `$width`) felhasználhatók.

**Korlátok / ellenőrzendők:** Nem volt lehetőség Unity buildet futtatni, így a fordítás és a vizuális működés még nincs igazolva. Az IMGUI vezérlés első implementáció; a fogantyúk képernyővetítésesek, nem a Unity Editor Handles rendszerét használják. A mentett `cabinetDesign` XML nem azonos a régi `cabinetType` XML-lel, és a szoba 3D nézete még nem importálja automatikusan a létrehozott szekrényeket.

# v0.2.5 火、水、管線驗證

Windows 玩家版位於 [FireRescue.exe](/D:/Documents/畢專/Builds/Local-v0.2.5/FireRescue.exe)，Unity 6000.5.0b9、Windows x64、D3D11，建置總大小 96,838,310 bytes。未提交或發布。

## 本輪修改

- 水滴與飛濺先把水量交給真正碰到的承接表面。普通火只從所在濕區取得冷卻，移除第一關落地時額外套用的 0.9m 滅火範圍。靜止及流動水使用同一份表面水量，熱交換扣掉的水量轉入蒸發帳。
- 冷地板不消耗水或出蒸汽。未達燃燒門檻的熱可消耗薄水膜，但不加入白色蒸汽額度；真正燃燒接觸才有短暫白蒸汽。不同火格解析度以同物理面積計算冷卻。水格到火格中心另做實際遮擋檢查，避免薄牆兩側共用粗火格而隔牆滅火。
- 蘇打粉在採用此水系統的場景也使用真正表面瞄準，求出初始彈道後照既有重力、阻力、射程及碰撞飛行。原固定仰角會把部分粉末射過近處火格。出粉量與反應倍率維持原值，油鍋仍須用蘇打粉。
- 回收管線原本錯誤反轉倖存接觸點，快速轉向時安全建路失敗，管端因而落後角色。保留「自由接頭到玩家」的順序後，回收端持續跟隨真正噴口接頭。10m 管長、持續外拉約 0.65s 脫開及 1.3s 慢快慢回收維持原值。

## 程式／玩家版驗證

| 情境 | 結果與證據 |
| --- | --- |
| 停噴後積水遇火 | 原反例強度 0.65 完全不降；修正後到 0，0.028889 水量由表面轉為蒸發，無重複計量。`Logs/Physics025/player-surface/results.txt` |
| 薄牆與蒸汽門檻 | 1.8cm 牆跨越同一粗火格時不隔牆冷卻。多個 0.04 預燃熱格合計蒸發 0.028444 水量，仍為零白蒸汽粒子。單粗格／四細格同面積、同投入水量的蒸發均為 0.14222。 |
| 真正檯面／油鍋 | 實際碰撞及檯面積水通過，完整櫃體下方乾燥。水命中油鍋仍只爆燃一次且不熄滅。`Logs/Physics025/player-counter/results.txt` |
| 水灘生命週期 | 兩區積水由掃射連成一區，隔牆及櫃底乾燥，停止、拔管與換斧後空中水繼續落地，冷水灘保留。`Logs/Physics025/puddles-final/results.txt` |
| 更新頻率 | 30／120Hz 的冷水投入與落地均為 6.00000。水火耦合投入均 6.00000、蒸發均 0.10795、表面剩餘均 5.89205。`Logs/Physics025/steam-final/steam/results.txt` |
| 管線 | 原繞角／張力／放開／重接測試通過。219 畫面檢查真實及可見牆網格皆零穿透；追加 15Hz 移動回收的 49 次可見管段檢查零穿透，接頭最大誤差 0.0000m。`Logs/Physics025/player-hose/results.txt` |
| 第一關 | 正常輸入命令完成馬桶水、普通火通道、倉庫粉末、油鍋、布丁、破門與 NPC。此次先用水保護相連撤離走廊，交付布丁時剩 27.0s，玩家抵達出口外後再等 4.9s 完成救援，完成事件一次。水量帳誤差 0.0000000。`Logs/Physics025/player-full/results.txt` |

這次的 4.9s 與舊腳本的 266.5s 使用不同消防走法。舊腳本未清撤離通道而等待全屋燃料耗盡，不能把差異宣稱為 NPC 速度修正。NPC、期限、地圖及管長均未修改。

第一關玩家版水模擬 CPU p95 為 0.318ms，峰值 201 個主要水滴與 75 個飛濺；管身 CPU 更新 p95 為 0.825ms。前者不含發射器、網格／貼圖更新及繪製，後者不含碰撞驗收及錄影。這些數字沒有證明整機 FPS 或 GPU 成本。

## 代理畫面檢查

以下完整片段取自 v0.2.5 Windows 玩家版的原遊戲相機，1024×576，白地板、米色牆及原光照。保留起噴、轉向、停噴、碰撞及殘留，沒有只選單張最好看的畫面。

- [冷地板與停噴殘留](/D:/Documents/畢專/Docs/Validation/2026-11-20-physics-finish/cold-floor.gif)
- [火接觸、蒸汽與熄滅](/D:/Documents/畢專/Docs/Validation/2026-11-20-physics-finish/fire-contact.gif)
- [檯面掃射與殘水](/D:/Documents/畢專/Docs/Validation/2026-11-20-physics-finish/countertop.gif)
- [拉緊、斷開與慢快慢收回](/D:/Documents/畢專/Docs/Validation/2026-11-20-physics-finish/taut-retract.gif)
- [NPC 由通道撤離](/D:/Documents/畢專/Docs/Validation/2026-11-20-physics-finish/npc-rescue.gif)

代理已檢查玩家版畫面及連續擷取序列。使用者尚未接受本輪外觀、手感與撤離走法。回收途中跨越另一個完整門口尚未獨立覆蓋；目前測到原廚房繞角、移動及快速轉向。整機 GPU／FPS 與真人遊玩仍需另行驗收。

第一關場景檔 SHA256 維持 `A0426EC089414DCE09647A34BA75BC736FE3457F7DE89CC9F7A2B0FAC2696DC2`。

主要程式為 `WaterLab.cs`、`WaterLabSurface.cs`、`FireField.cs`、`SprayEmitter.cs`、`LocalPlayerInput.cs` 及 `HoseWrapPath.cs`。獨立重現／驗證保留於 `ReviewWaterSmoke`、其兩個 partial 測試檔及 `WaterLabValidation.cs`。

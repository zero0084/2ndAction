# 属性カード20枚の性能評価表(2026-10-30、次の性能調整の資料)
#   python elemtable.py <cards の elem.tsv> <real の elem.tsv> <カード資料の raw/cards.tsv> <出力 md>
#  値: Lv1/Lv9 の効果・レア度 = カード資料(ゲームの計算と同じ)/ 発動率・ダメージ = 1枚だけ持って倒れうる雑魚に100回当てた試験
#  (-qaElemVis -evPart cards)/ 実戦 = 系統を全部 Lv9 で 22km から90秒(-evPart real)。評価はアイリーンの見立て(相談の叩き台)
import csv, sys
cards_tsv, real_tsv, cat_tsv, out = sys.argv[1:5]
def rows(p): return list(csv.DictReader(open(p, encoding='utf-8'), delimiter='\t', quoting=csv.QUOTE_NONE))
meas = {(r['element'], r['case']): r for r in rows(cards_tsv)}
real = {r['element']: r for r in rows(real_tsv) if r['part'] == 'real'}
cat = {r['id']: r for r in rows(cat_tsv)}
FAM = [('炎', 'fire', ['flame_blade', 'burning_soul', 'inferno', 'flame_counter']),
       ('氷', 'ice', ['frost_edge', 'ice_prison', 'absolute_zero']),
       ('雷', 'thunder', ['thunder_strike', 'chain_lightning', 'high_voltage', 'thunder_lord']),
       ('風', 'wind', ['wind_cutter', 'gale', 'tornado']),
       ('血', 'blood', ['vampire', 'predator', 'overheal', 'blood_rush', 'blood_blade', 'phoenix'])]
LOOK = {
 'fire': '付与で炎がボッ、炎上中は体に揺れる炎、火の粉が上へ、頭上に炎のマーク',
 'ice': '冷えで結晶が光る・足元に霜、凍結は体の下と端に氷・氷片、雪/氷塊のマーク',
 'thunder': '淡黄の芯+金の縁の稲妻、連鎖は線で追える、帯電の電気、稲妻のマーク',
 'wind': '濃い縁の風刃・プレイヤーに風の線、当たると渦、竜巻は渦の柱と風の輪',
 'blood': '出血は赤黒い滴が下へ、吸収/回復は敵からプレイヤーへ粒が戻り回復の光、滴のマーク'}
SPECIAL = {
 'inferno': '延焼(炎上中の敵を倒した時)', 'flame_counter': 'Shield で防いだ時だけ', 'absolute_zero': '凍結の時の砕け(凍結には冷えを5回)',
 'chain_lightning': '連鎖', 'thunder_lord': '連鎖・範囲', 'tornado': '風刃の命中で竜巻', 'vampire': '撃破で吸収',
 'predator': 'ボスへの命中でも吸収', 'overheal': '溢れた回復→Shield', 'blood_rush': 'HP50%/25%以下', 'phoenix': '倒れた時に復活'}
# 強化が必要か(アイリーンの見立て)
NEED = {
 'flame_blade': ('不要〜小', '入口として発動は十分(Lv9で100回中56回)。★4・20km解放が遅い方が問題'),
 'burning_soul': ('要', '単独では確率7〜18%・威力が小さい。入口なしでは働きが見えにくい'),
 'inferno': ('要', '単独の炎上は確率5%固定・Lv9でも伸びない。延焼は撃破が条件で、試験では Lv9 で0回'),
 'flame_counter': ('要(条件の見直し)', 'Shield で防いだ時だけ。普段の戦いではほぼ出ない(試験で0回)'),
 'frost_edge': ('小', '冷えは Lv9 で100回中55回と十分。ダメージは無いので「止める」価値を出す調整を'),
 'ice_prison': ('小', '凍結の回数は伸びる(Lv9 で9回)。単独の冷えの確率が低い'),
 'absolute_zero': ('要', '単独では冷え5%固定→凍結に届かず、砕けは0回。入口とセットでしか働かない'),
 'thunder_strike': ('要', 'Lv1 で100回中5回・合計40(1発の2発分)。入口として弱い'),
 'chain_lightning': ('要', '単独の落雷は4%固定・威力3。連鎖は見えるが元の落雷が少ない'),
 'high_voltage': ('要', '単独の落雷は1〜10%・ダメージは合計30まで。停止は雑魚だけ'),
 'thunder_lord': ('要', '単独の落雷は2〜4%。範囲・連鎖は入口とセットで生きる'),
 'wind_cutter': ('小', 'Lv9 で100回中32回・全ダメージの37%。入口として機能'),
 'gale': ('小〜要', '単独の風刃は4〜19%。射程・貫通の差は見えるが量が少ない'),
 'tornado': ('要', '単独の風刃は4%固定→竜巻も2〜3回。入口とセットでしか働かない'),
 'vampire': ('要', '撃破で判定(100回の命中・4体撃破で2回だけ)。命中では回復しない'),
 'predator': ('要', 'ボス以外では VAMPIRE と同じ。ボスへの吸収は確率1/10'),
 'overheal': ('役割の見直し', '回復の手段が無いと何も起きない(試験で0回)'),
 'blood_rush': ('役割の見直し', 'HP が低い時だけ(試験は満HPなので0回)。属性としての手触りが無い'),
 'blood_blade': ('小', '出血は満HPで確率2倍、Lv9で毎回。ただしダメージは全体の13%'),
 'phoenix': ('別枠', '復活のカード。属性の強化とは別に扱う方がよい'),
}
L = []
A = L.append
A('# 属性カード20枚の性能評価表(2026-10-30)\n')
A('次の段階(属性20枚の性能とレア度の再設計)の資料です。**今回は性能を変えていません。** 視認性の改修後に測った値です。\n')
A('- 発動率・ダメージ: そのカード**1枚だけ**(入口のカードなし)を持った黒剣士(Lv1・ほかのカードなし)が、HP400 の雑魚3体(20回ほどで倒れる)に前攻撃を100回当てた時。通常の1発 = 20。')
A('- 実戦: 系統を全部 Lv9 で荒野街道 22km から90秒(補助+前への攻撃)。系統ごとの値なので、その系統の行に同じ値を載せています。')
A('- 100回の試験なので、確率が5%前後のカードは回数の揺れが大きい(例: INFERNO の単独の炎上は確率5%固定なのに、Lv1 で8回・Lv9 で1回)。Lv で値が変わらない所は Lv1/Lv9 の欄で見てください。')
A('- 「強化が必要か」はアイリーンの見立て(相談の叩き台)。\n')
A('| 属性 | カード | ★ | Lv1 | Lv9 | 100回の発動 Lv1→Lv9 | 追加ダメージ Lv1→Lv9 | 継続時間など | 実戦(系統 Lv9・90秒) | 今の見た目 | 強化が必要か | 理由 |')
A('|---|---|---|---|---|---|---|---|---|---|---|---|')
for ja, key, ids in FAM:
    r = real.get(key, {})
    realtxt = f"発動 {r.get('procs','-')}回/命中{r.get('hits','-')}・属性ダメージ {r.get('elemDamage','-')}({r.get('sharePct','-')}%)・倒した{r.get('kills','-')}体" if r else '-'
    for cid in ids:
        c = cat[cid]; m1 = meas.get((cid, 'lv1'), {}); m9 = meas.get((cid, 'lv9'), {})
        p = f"{m1.get('per100','-')} → {m9.get('per100','-')}"
        d = f"{m1.get('elemDamage','-')} → {m9.get('elemDamage','-')}"
        extra = SPECIAL.get(cid, '')
        if cid in ('frost_edge', 'ice_prison'): extra = (extra + ' ' if extra else '') + f"凍結 Lv9 {m9.get('freezes','0')}回"
        if cid in ('chain_lightning', 'thunder_lord'): extra += f" / 連鎖 Lv9 {m9.get('chains','0')}本"
        if cid == 'tornado': extra += f" / 竜巻 Lv9 {m9.get('tornados','0')}回"
        if cid == 'inferno': extra += f" / 延焼 Lv1 {m1.get('infernos','0')}回・Lv9 {m9.get('infernos','0')}回"
        if cid in ('vampire', 'predator'): extra += f" / 吸収 Lv9 {m9.get('lifesteal','0')}回"
        need, why = NEED[cid]
        A(f"| {ja} | {c['name']} | {c['rarity']} | {c['lv1']} | {c['lv9']} | {p} | {d} | {extra} | {realtxt} | {LOOK[key]} | {need} | {why} |")
A('\n## まとめ(アイリーンの見立て)')
A('- **見えるようになった上で、弱いと分かったもの**: 雷の入口(THUNDER STRIKE Lv1 は100回で5回・合計40)、上位カードの単独(INFERNO・ABSOLUTE ZERO・CHAIN LIGHTNING・TORNADO は単独だと確率と威力が固定で伸びない)、血の吸収(撃破が条件で、命中では回復しない)。')
A('- **系統を揃えると強い**: 炎・雷・風は系統 Lv9 で全ダメージの5〜7割を属性が占める(Lv1・ほかのカードなしの測り方なので、普通のビルドでは割合は下がる)。上位カードは「入口との組み合わせ」でしか働かない作りになっている。')
A('- **条件のカード**: FLAME COUNTER(Shield で防ぐ)・OVERHEAL(回復が必要)・BLOOD RUSH(低HP)・PHOENIX(倒れる)は、属性の手触りとは別の役割。属性の系統に入れるかどうかから相談したい。')
A('- レア度: 入口4枚が★4(20km)で、上位寄りの★3(5km)より後に手に入る逆転(カード資料 2026-10-30)。')
open(out, 'w', encoding='utf-8').write('\n'.join(L) + '\n')
print('ok', out)

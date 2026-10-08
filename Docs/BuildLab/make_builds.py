# -*- coding: utf-8 -*-
# ビルド検証(依頼F)のビルド定義を作る: 12キャラ × 4方向(A 攻撃 / D 耐久・回復 / B バランス / S 得意能力・相乗)。
# deck = デッキ12枚(この順がラン中の選択の優先順位), charCards = キャラカード3枠(育成段階のLvで付ける)。
# 出力: builds_v1.json(QaSweep -qaBuildLab -blBuilds で読む)
import json, io, os, sys

HERE = os.path.dirname(os.path.abspath(__file__))
CARDS = set()
cards_tsv = sys.argv[1] if len(sys.argv) > 1 else None
if cards_tsv:
    for line in io.open(cards_tsv, encoding='utf-8'):
        CARDS.add(line.split('\t')[0])

A = ['attack_up', 'attack_speed_up', 'boss_killer', 'double_attack', 'sonic_blade', 'first_strike', 'combo_plus', 'long_blade', 'rapid_edge', 'shockwave', 'brake_attack', 'speed_up']
D = ['heart_up', 'shield', 'vampire', 'predator', 'iron_will', 'second_wind', 'overheal', 'phoenix', 'last_chance', 'perfect_guard', 'heavy_armor', 'long_haul']
B = ['attack_up', 'heart_up', 'boss_killer', 'shield', 'attack_speed_up', 'vampire', 'sonic_blade', 'second_wind', 'double_attack', 'phoenix', 'iron_will', 'speed_up']
S = {
    'swordsman':     ['combo_plus', 'combo_rush', 'attack_up', 'boss_killer', 'first_strike', 'combo_master', 'sonic_blade', 'attack_speed_up', 'heart_up', 'shield', 'vampire', 'speed_up'],
    'dual_blade':    ['combo_edge', 'attack_speed_up', 'double_attack', 'combo_rush', 'attack_up', 'boss_killer', 'rapid_edge', 'blood_blade', 'vampire', 'heart_up', 'shield', 'speed_up'],
    'noble_lady':    ['jump_count_up', 'speed_up', 'ground_fighter', 'heart_up', 'attack_up', 'boss_killer', 'shield', 'iron_will', 'attack_speed_up', 'ground_zero', 'sonic_blade', 'jump_power_up'],
    'gunslinger':    ['piercing_blade', 'attack_speed_up', 'attack_up', 'boss_killer', 'shockwave', 'rapid_edge', 'double_attack', 'attack_range_up', 'heart_up', 'shield', 'vampire', 'speed_up'],
    'dragon_lancer': ['attack_speed_up', 'rapid_edge', 'attack_up', 'boss_killer', 'first_strike', 'long_blade', 'heavy_impact', 'ground_breaker', 'heart_up', 'shield', 'vampire', 'speed_up'],
    'archer':        ['speed_up', 'piercing_blade', 'attack_speed_up', 'attack_up', 'boss_killer', 'rapid_edge', 'wind_cutter', 'gale', 'heart_up', 'shield', 'vampire', 'double_attack'],
    'mage':          ['heart_up', 'shield', 'thunder_strike', 'attack_up', 'boss_killer', 'high_voltage', 'chain_lightning', 'attack_speed_up', 'vampire', 'phoenix', 'iron_will', 'speed_up'],
    'fighter':       ['attack_range_up', 'long_blade', 'combo_rush', 'attack_up', 'boss_killer', 'combo_plus', 'ground_fighter', 'ground_breaker', 'heart_up', 'shield', 'vampire', 'speed_up'],
    'ninja':         ['speed_up', 'sonic_blade', 'first_strike', 'attack_up', 'boss_killer', 'momentum', 'overdrive', 'close_call', 'attack_speed_up', 'heart_up', 'shield', 'vampire'],
    'miko':          ['shield', 'attack_speed_up', 'attack_up', 'boss_killer', 'counter', 'perfect_guard', 'piercing_blade', 'shockwave', 'heart_up', 'vampire', 'phoenix', 'speed_up'],
    'vampire':       ['vampire', 'predator', 'attack_up', 'boss_killer', 'blood_blade', 'blood_rush', 'attack_speed_up', 'double_attack', 'heart_up', 'shield', 'last_stand', 'speed_up'],
    'dragonkin':     ['attack_up', 'boss_killer', 'heavy_impact', 'ground_breaker', 'iron_will', 'heart_up', 'sonic_blade', 'shield', 'vampire', 'attack_speed_up', 'second_wind', 'speed_up'],
}
CHARS = ['swordsman', 'dual_blade', 'noble_lady', 'gunslinger', 'dragon_lancer', 'archer', 'mage', 'fighter', 'ninja', 'miko', 'vampire', 'dragonkin']
SLOW = {'noble_lady', 'archer', 'dragonkin'}  # 自然の最高速が補助の開始(100km/h)に届かない(移動 0.93〜0.95)

def deck_for(ch, t):
    d = list({'A': A, 'D': D, 'B': B, 'S': S[ch]}[t])
    # 足の遅いキャラは、どの型でも speed_up を入れる(補助の開始速度に届かせる)。入っていなければ最後の1枚と入れ替え
    if ch in SLOW and 'speed_up' not in d: d[-1] = 'speed_up'
    return d

NOTE = {'A': '攻撃重視', 'D': '耐久・回復重視', 'B': 'バランス', 'S': 'キャラの得意能力・相乗'}
CHARCARDS = {'A': ['attack_up', 'boss_killer', 'attack_speed_up'], 'D': ['heart_up', 'shield', 'vampire'], 'B': ['attack_up', 'heart_up', 'boss_killer']}
builds = []
for ch in CHARS:
    for t in 'ADBS':
        deck = deck_for(ch, t)
        assert len(deck) == 12 and len(set(deck)) == 12, (ch, t, deck)
        cc = CHARCARDS.get(t, deck[:3])
        if CARDS:
            bad = [c for c in deck + cc if c not in CARDS]
            assert not bad, (ch, t, bad)
        builds.append({'name': f'{ch}_{t}', 'character': ch, 'type': t, 'note': NOTE[t], 'deck': deck, 'charCards': cc, 'priority': deck, 'takeFinalEvolution': True})
out = os.path.join(HERE, 'builds_v1.json')
io.open(out, 'w', encoding='utf-8').write(json.dumps({'builds': builds}, ensure_ascii=False, indent=1))
print(len(builds), 'builds ->', out)

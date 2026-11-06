# -*- coding: utf-8 -*-
# 2026-11-06 カード編集画面の絞り込み・並び替え(ID 10460〜10469)の訳の下書き(アイリーン)。
# エステルのレビュー用に omm_tr_cf.tsv(lang\tid\ttext)と Docs/Cards_2026-11-06/loc_review/review_jp_en.tsv を書き出す。
# 日本語/英語は keys_extra_cardfilter.tsv + out/en.cf.tsv で接続済み(merge_add.py cf keys_extra_cardfilter.tsv)。
# レビュー後は out/<code>.cf2.tsv を作って merge_add.py cf2 keys_extra_cardfilter.tsv で取り込む
import io, os
HERE = os.path.dirname(os.path.abspath(__file__))
DOC = os.path.abspath(os.path.join(HERE, '..', '..', '..', 'Docs', 'Cards_2026-11-06', 'loc_review'))
IDS = list(range(10460, 10470))
T = {
'en': ["COMMON", "WILDERNESS", "CAVE", "SKY", "LAST DUNGEON", "Element", "Map", "Unlock order", "Locked: {0}", "No cards match these filters"],
'zh-Hans': ["通用", "荒野", "洞窟", "天空", "最终迷宫", "属性", "地图", "解锁顺序", "未解锁 {0}", "没有符合条件的卡牌"],
'zh-Hant': ["通用", "荒野", "洞窟", "天空", "最終迷宮", "屬性", "地圖", "解鎖順序", "未解鎖 {0}", "沒有符合條件的卡牌"],
'ko': ["공통", "황야", "동굴", "천공", "라스트 던전", "속성", "맵", "해금순", "미해금 {0}", "조건에 맞는 카드가 없습니다"],
'id': ["UMUM", "TANDUS", "GUA", "LANGIT", "DUNGEON AKHIR", "Elemen", "Peta", "Urutan buka", "Terkunci: {0}", "Tidak ada kartu yang cocok"],
'ms': ["UMUM", "GERSANG", "GUA", "LANGIT", "DUNGEON AKHIR", "Elemen", "Peta", "Urutan buka kunci", "Terkunci: {0}", "Tiada kad yang sepadan"],
'vi': ["CHUNG", "HOANG ĐỊA", "HANG ĐỘNG", "BẦU TRỜI", "HẦM NGỤC CUỐI", "Nguyên tố", "Bản đồ", "Thứ tự mở khóa", "Chưa mở: {0}", "Không có thẻ nào phù hợp"],
'th': ["ทั่วไป", "แดนรกร้าง", "ถ้ำ", "ฟ้า", "ดันเจี้ยนสุดท้าย", "ธาตุ", "แผนที่", "ลำดับปลดล็อก", "ยังล็อก {0}", "ไม่มีการ์ดที่ตรงเงื่อนไข"],
'fil': ["KARANIWAN", "ILANG", "KUWEBA", "LANGIT", "HULING DUNGEON", "Elemento", "Mapa", "Ayos ng pag-unlock", "Naka-lock: {0}", "Walang kard na tugma"],
'my': ["အထွေထွေ", "မြေရိုင်း", "ဂူ", "ကောင်းကင်", "နောက်ဆုံးဒန်ဂျွန်", "ဓာတ်", "မြေပုံ", "ဖွင့်သည့်အစဉ်", "မဖွင့်ရသေး {0}", "ကိုက်ညီသောကတ် မရှိပါ"],
'km': ["ទូទៅ", "វាលរហោស្ថាន", "រូងភ្នំ", "មេឃ", "ឌុងហ្គឺនចុងក្រោយ", "ធាតុ", "ផែនទី", "លំដាប់ដោះសោ", "មិនទាន់ដោះសោ {0}", "គ្មានកាតត្រូវលក្ខខណ្ឌ"],
'lo': ["ທົ່ວໄປ", "ທົ່ງຮ້າງ", "ຖ້ຳ", "ທ້ອງຟ້າ", "ດັນເຈີນສຸດທ້າຍ", "ທາດ", "ແຜນທີ່", "ລຳດັບປົດລັອກ", "ຍັງລັອກ {0}", "ບໍ່ມີບັດທີ່ກົງເງື່ອນໄຂ"],
'hi': ["सामान्य", "बंजर", "गुफा", "आकाश", "अंतिम कालकोठरी", "तत्व", "नक्शा", "अनलॉक क्रम", "लॉक: {0}", "इन फ़िल्टरों से मेल खाता कोई कार्ड नहीं"],
'bn': ["সাধারণ", "বিরান", "গুহা", "আকাশ", "শেষ ডানজন", "উপাদান", "মানচিত্র", "আনলকের ক্রম", "লক: {0}", "মিলে যাওয়া কোনো কার্ড নেই"],
'ta': ["பொது", "பாழ்நிலம்", "குகை", "வானம்", "இறுதி நிலவறை", "தனிமம்", "வரைபடம்", "திறப்பு வரிசை", "பூட்டியவை: {0}", "பொருந்தும் அட்டைகள் இல்லை"],
'fr': ["COMMUN", "LANDES", "GROTTE", "CIEL", "DONJON FINAL", "Élément", "Carte", "Ordre de déblocage", "Verrouillées : {0}", "Aucune carte ne correspond"],
'de': ["ALLGEMEIN", "ÖDLAND", "HÖHLE", "HIMMEL", "LETZTER DUNGEON", "Element", "Karte", "Freischaltreihenfolge", "Gesperrt: {0}", "Keine passenden Karten"],
'es': ["COMÚN", "PÁRAMO", "CUEVA", "CIELO", "MAZMORRA FINAL", "Elemento", "Mapa", "Orden de desbloqueo", "Bloqueadas: {0}", "Ninguna carta coincide"],
'pt-BR': ["COMUM", "ERMO", "CAVERNA", "CÉU", "MASMORRA FINAL", "Elemento", "Mapa", "Ordem de desbloqueio", "Bloqueadas: {0}", "Nenhuma carta corresponde"],
'it': ["COMUNI", "DESOLATA", "CAVERNA", "CIELO", "DUNGEON FINALE", "Elemento", "Mappa", "Ordine di sblocco", "Bloccate: {0}", "Nessuna carta corrisponde"],
'pl': ["WSPÓLNE", "PUSTKOWIE", "JASKINIA", "NIEBO", "OSTATNI LOCH", "Żywioł", "Mapa", "Kolejność odblokowania", "Zablokowane: {0}", "Brak pasujących kart"],
'uk': ["СПІЛЬНІ", "ПУСТКИ", "ПЕЧЕРА", "НЕБО", "ОСТАННЄ ПІДЗЕМЕЛЛЯ", "Стихія", "Мапа", "Порядок розблокування", "Заблоковано: {0}", "Немає відповідних карт"],
'ru': ["ОБЩИЕ", "ПУСТОШИ", "ПЕЩЕРА", "НЕБО", "ПОСЛЕДНЕЕ ПОДЗЕМЕЛЬЕ", "Стихия", "Карта", "Порядок разблокировки", "Заблокировано: {0}", "Нет подходящих карт"],
'tr': ["ORTAK", "ÇORAK", "MAĞARA", "GÖK", "SON ZİNDAN", "Element", "Harita", "Kilit açma sırası", "Kilitli: {0}", "Eşleşen kart yok"],
'cs': ["SPOLEČNÉ", "PUSTINA", "JESKYNĚ", "NEBE", "POSLEDNÍ KOBKA", "Živel", "Mapa", "Pořadí odemčení", "Zamčeno: {0}", "Žádné odpovídající karty"],
'ro': ["COMUNE", "PUSTIU", "PEȘTERĂ", "CER", "TEMNIȚA FINALĂ", "Element", "Hartă", "Ordinea deblocării", "Blocate: {0}", "Nicio carte nu se potrivește"],
'ar': ["عام", "البراري", "الكهف", "السماء", "الزنزانة الأخيرة", "العنصر", "الخريطة", "ترتيب الفتح", "مقفلة: {0}", "لا توجد بطاقات مطابقة"],
'fa': ["عمومی", "بیابان", "غار", "آسمان", "سیاه‌چال آخر", "عنصر", "نقشه", "ترتیب باز شدن", "قفل: {0}", "کارتی مطابق نیست"],
'sw': ["ZA KAWAIDA", "NYIKA", "PANGO", "ANGA", "SHIMO LA MWISHO", "Elementi", "Ramani", "Mpangilio wa kufungua", "Imefungwa: {0}", "Hakuna kadi zinazolingana"],
}
keys = {}
for line in io.open(os.path.join(HERE, '..', 'keys_extra_cardfilter.tsv'), encoding='utf-8'):
    p = line.rstrip('\n').split('\t')
    if len(p) == 4: keys[int(p[0])] = (p[2], p[3])
os.makedirs(DOC, exist_ok=True)
with io.open(os.path.join(HERE, 'omm_tr_cf.tsv'), 'w', encoding='utf-8', newline='\n') as f:
    for code, vals in T.items():
        assert len(vals) == len(IDS), code
        for i, v in zip(IDS, vals): f.write(f"{code}\t{i}\t{v}\n")
with io.open(os.path.join(DOC, 'review_jp_en.tsv'), 'w', encoding='utf-8', newline='\n') as f:
    f.write("id\tcategory\tnote\tja\ten\n")
    for n, i in enumerate(IDS): f.write(f"{i}\tui\t{keys[i][0]}\t{keys[i][1]}\t{T['en'][n]}\n")
with io.open(os.path.join(DOC, 'draft_30lang.tsv'), 'w', encoding='utf-8', newline='\n') as f:
    f.write("lang\tid\tja\ttext\n")
    for code, vals in T.items():
        for i, v in zip(IDS, vals): f.write(f"{code}\t{i}\t{keys[i][1]}\t{v}\n")
print('ok', len(T), 'languages')

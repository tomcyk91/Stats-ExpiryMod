namespace StatisticMod
{
    internal static partial class ModLocalization
    {
        /// <summary>
        /// Checkout Analytics vocabulary for every non-English/non-Polish
        /// language currently supported by the mod.
        /// </summary>
        private static void AddCheckoutAnalyticsTerms()
        {
            SetCheckoutAnalyticsTerms("fr",
                "Caisses", "Revenu des caisses", "Carte", "Espèces", "Caisse automatique",
                "Caissier", "Joueur", "Caisse classique",
                "Répartition du revenu disponible à partir de cette version");

            SetCheckoutAnalyticsTerms("it",
                "Casse", "Ricavi casse", "Carta", "Contanti", "Cassa automatica",
                "Cassiere", "Giocatore", "Cassa tradizionale",
                "Ripartizione dei ricavi disponibile da questa versione");

            SetCheckoutAnalyticsTerms("de",
                "Kassen", "Kassenumsatz", "Karte", "Bar", "Selbstbedienungskasse",
                "Kassierer", "Spieler", "Normale Kasse",
                "Umsatzaufteilung ab dieser Version verfügbar");

            SetCheckoutAnalyticsTerms("es",
                "Cajas", "Ingresos de caja", "Tarjeta", "Efectivo", "Autocobro",
                "Cajero", "Jugador", "Caja tradicional",
                "Desglose de ingresos disponible desde esta versión");

            SetCheckoutAnalyticsTerms("zh",
                "收银台", "收银收入", "银行卡", "现金", "自助结账",
                "收银员", "玩家", "普通收银台",
                "收入分类数据从此版本开始提供");

            SetCheckoutAnalyticsTerms("pt-BR",
                "Caixas", "Receita dos caixas", "Cartão", "Dinheiro", "Autoatendimento",
                "Caixa", "Jogador", "Caixa tradicional",
                "Divisão da receita disponível a partir desta versão");

            SetCheckoutAnalyticsTerms("nl",
                "Kassa's", "Kassa-omzet", "Kaart", "Contant", "Zelfscan",
                "Kassamedewerker", "Speler", "Gewone kassa",
                "Omzetverdeling beschikbaar vanaf deze versie");

            SetCheckoutAnalyticsTerms("ja",
                "レジ", "レジ売上", "カード", "現金", "セルフレジ",
                "レジ係", "プレイヤー", "通常レジ",
                "売上内訳はこのバージョン以降で利用できます");

            SetCheckoutAnalyticsTerms("ko",
                "계산대", "계산대 매출", "카드", "현금", "셀프 계산대",
                "계산원", "플레이어", "일반 계산대",
                "매출 분류 데이터는 이 버전부터 제공됩니다");

            SetCheckoutAnalyticsTerms("pt-PT",
                "Caixas", "Receita das caixas", "Cartão", "Dinheiro", "Caixa automática",
                "Caixa", "Jogador", "Caixa tradicional",
                "Divisão da receita disponível a partir desta versão");

            SetCheckoutAnalyticsTerms("ru",
                "Кассы", "Выручка касс", "Карта", "Наличные", "Самообслуживание",
                "Кассир", "Игрок", "Обычная касса",
                "Разбивка выручки доступна начиная с этой версии");

            SetCheckoutAnalyticsTerms("tr",
                "Kasalar", "Kasa geliri", "Kart", "Nakit", "Otomatik kasa",
                "Kasiyer", "Oyuncu", "Normal kasa",
                "Gelir dağılımı bu sürümden itibaren kullanılabilir");

            SetCheckoutAnalyticsTerms("da",
                "Kasser", "Kasseomsætning", "Kort", "Kontant", "Selvbetjening",
                "Kassemedarbejder", "Spiller", "Almindelig kasse",
                "Omsætningsfordeling er tilgængelig fra denne version");

            SetCheckoutAnalyticsTerms("fi",
                "Kassat", "Kassojen liikevaihto", "Kortti", "Käteinen", "Itsepalvelukassa",
                "Kassatyöntekijä", "Pelaaja", "Tavallinen kassa",
                "Liikevaihdon jako on käytettävissä tästä versiosta alkaen");

            SetCheckoutAnalyticsTerms("hu",
                "Pénztárak", "Pénztári bevétel", "Kártya", "Készpénz", "Önkiszolgáló pénztár",
                "Pénztáros", "Játékos", "Hagyományos pénztár",
                "A bevételbontás ettől a verziótól érhető el");

            SetCheckoutAnalyticsTerms("ro",
                "Case", "Venit la case", "Card", "Numerar", "Casă self-service",
                "Casier", "Jucător", "Casă tradițională",
                "Defalcarea veniturilor este disponibilă începând cu această versiune");

            SetCheckoutAnalyticsTerms("cs",
                "Pokladny", "Tržby pokladen", "Karta", "Hotovost", "Samoobslužná pokladna",
                "Pokladní", "Hráč", "Běžná pokladna",
                "Rozdělení tržeb je dostupné od této verze");

            SetCheckoutAnalyticsTerms("lt",
                "Kasos", "Kasų pajamos", "Kortelė", "Grynieji", "Savitarnos kasa",
                "Kasininkas", "Žaidėjas", "Įprasta kasa",
                "Pajamų išskaidymas galimas nuo šios versijos");

            SetCheckoutSalesChannelTerms("fr",
                "Toutes les transactions", "Revenu total", "Transactions en caisse",
                "Commandes en ligne", "Distributeurs automatiques", "Transaction moyenne",
                "Données précises sur le mode de paiement disponibles à partir de cette version",
                "Données des canaux de vente disponibles à partir de cette version",
                "Caisses, commandes en ligne et distributeurs automatiques",
                "Caisses et commandes en ligne",
                "Revenu de tous les canaux de vente suivis");

            SetCheckoutSalesChannelTerms("it",
                "Tutte le transazioni", "Ricavi totali", "Transazioni in cassa",
                "Ordini online", "Distributori automatici", "Transazione media",
                "Dati accurati sul metodo di pagamento disponibili da questa versione",
                "Dati sui canali di vendita disponibili da questa versione",
                "Casse, ordini online e distributori automatici",
                "Casse e ordini online",
                "Ricavi da tutti i canali di vendita monitorati");

            SetCheckoutSalesChannelTerms("de",
                "Alle Transaktionen", "Gesamtumsatz", "Kassentransaktionen",
                "Online-Bestellungen", "Verkaufsautomaten", "Ø Transaktion",
                "Genaue Zahlungsartdaten sind ab dieser Version verfügbar",
                "Vertriebskanaldaten sind ab dieser Version verfügbar",
                "Kassen, Online-Bestellungen und Verkaufsautomaten",
                "Kassen und Online-Bestellungen",
                "Umsatz aus allen erfassten Vertriebskanälen");

            SetCheckoutSalesChannelTerms("es",
                "Todas las transacciones", "Ingresos totales", "Transacciones de caja",
                "Pedidos online", "Máquinas expendedoras", "Transacción media",
                "Los datos precisos del método de pago están disponibles desde esta versión",
                "Los datos de canales de venta están disponibles desde esta versión",
                "Cajas, pedidos online y máquinas expendedoras",
                "Cajas y pedidos online",
                "Ingresos de todos los canales de venta registrados");

            SetCheckoutSalesChannelTerms("zh",
                "全部交易", "总收入", "收银交易",
                "在线订单", "自动售货机", "平均交易额",
                "准确的支付方式数据从此版本开始提供",
                "销售渠道数据从此版本开始提供",
                "收银台、在线订单和自动售货机",
                "收银台和在线订单",
                "所有已跟踪销售渠道的收入");

            SetCheckoutSalesChannelTerms("pt-BR",
                "Todas as transações", "Receita total", "Transações de caixa",
                "Pedidos online", "Máquinas de venda automática", "Transação média",
                "Dados precisos do método de pagamento disponíveis a partir desta versão",
                "Dados dos canais de venda disponíveis a partir desta versão",
                "Caixas, pedidos online e máquinas de venda automática",
                "Caixas e pedidos online",
                "Receita de todos os canais de venda monitorados");

            SetCheckoutSalesChannelTerms("nl",
                "Alle transacties", "Totale omzet", "Kassatransacties",
                "Online bestellingen", "Verkoopautomaten", "Gem. transactie",
                "Nauwkeurige betaalmethodegegevens zijn beschikbaar vanaf deze versie",
                "Verkoopkanaalgegevens zijn beschikbaar vanaf deze versie",
                "Kassa's, online bestellingen en verkoopautomaten",
                "Kassa's en online bestellingen",
                "Omzet uit alle bijgehouden verkoopkanalen");

            SetCheckoutSalesChannelTerms("ja",
                "全取引", "総売上", "レジ取引",
                "オンライン注文", "自動販売機", "平均取引額",
                "正確な支払い方法データはこのバージョン以降で利用できます",
                "販売チャネルデータはこのバージョン以降で利用できます",
                "レジ、オンライン注文、自動販売機",
                "レジとオンライン注文",
                "追跡中のすべての販売チャネルからの売上");

            SetCheckoutSalesChannelTerms("ko",
                "전체 거래", "총매출", "계산대 거래",
                "온라인 주문", "자동판매기", "평균 거래액",
                "정확한 결제 방식 데이터는 이 버전부터 제공됩니다",
                "판매 채널 데이터는 이 버전부터 제공됩니다",
                "계산대, 온라인 주문 및 자동판매기",
                "계산대 및 온라인 주문",
                "추적 중인 모든 판매 채널의 매출");

            SetCheckoutSalesChannelTerms("pt-PT",
                "Todas as transações", "Receita total", "Transações de caixa",
                "Encomendas online", "Máquinas de venda automática", "Transação média",
                "Dados precisos do método de pagamento disponíveis a partir desta versão",
                "Dados dos canais de venda disponíveis a partir desta versão",
                "Caixas, encomendas online e máquinas de venda automática",
                "Caixas e encomendas online",
                "Receita de todos os canais de venda monitorizados");

            SetCheckoutSalesChannelTerms("ru",
                "Все транзакции", "Общая выручка", "Кассовые транзакции",
                "Онлайн-заказы", "Торговые автоматы", "Средняя транзакция",
                "Точные данные о способах оплаты доступны начиная с этой версии",
                "Данные по каналам продаж доступны начиная с этой версии",
                "Кассы, онлайн-заказы и торговые автоматы",
                "Кассы и онлайн-заказы",
                "Выручка по всем отслеживаемым каналам продаж");

            SetCheckoutSalesChannelTerms("tr",
                "Tüm işlemler", "Toplam gelir", "Kasa işlemleri",
                "Çevrim içi siparişler", "Otomatlar", "Ortalama işlem",
                "Doğru ödeme yöntemi verileri bu sürümden itibaren kullanılabilir",
                "Satış kanalı verileri bu sürümden itibaren kullanılabilir",
                "Kasalar, çevrim içi siparişler ve otomatlar",
                "Kasalar ve çevrim içi siparişler",
                "İzlenen tüm satış kanallarından elde edilen gelir");

            SetCheckoutSalesChannelTerms("da",
                "Alle transaktioner", "Samlet omsætning", "Kassetransaktioner",
                "Onlineordrer", "Salgsautomater", "Gns. transaktion",
                "Nøjagtige betalingsdata er tilgængelige fra denne version",
                "Data for salgskanaler er tilgængelige fra denne version",
                "Kasser, onlineordrer og salgsautomater",
                "Kasser og onlineordrer",
                "Omsætning fra alle sporede salgskanaler");

            SetCheckoutSalesChannelTerms("fi",
                "Kaikki tapahtumat", "Kokonaisliikevaihto", "Kassatapahtumat",
                "Verkkotilaukset", "Myyntiautomaatit", "Keskimääräinen tapahtuma",
                "Tarkat maksutapatiedot ovat käytettävissä tästä versiosta alkaen",
                "Myyntikanavatiedot ovat käytettävissä tästä versiosta alkaen",
                "Kassat, verkkotilaukset ja myyntiautomaatit",
                "Kassat ja verkkotilaukset",
                "Liikevaihto kaikista seuratuista myyntikanavista");

            SetCheckoutSalesChannelTerms("hu",
                "Összes tranzakció", "Teljes bevétel", "Pénztári tranzakciók",
                "Online rendelések", "Árusító automaták", "Átlagos tranzakció",
                "A pontos fizetési mód adatok ettől a verziótól érhetők el",
                "Az értékesítési csatornák adatai ettől a verziótól érhetők el",
                "Pénztárak, online rendelések és árusító automaták",
                "Pénztárak és online rendelések",
                "Bevétel az összes követett értékesítési csatornából");

            SetCheckoutSalesChannelTerms("ro",
                "Toate tranzacțiile", "Venit total", "Tranzacții la casă",
                "Comenzi online", "Automate de vânzare", "Tranzacție medie",
                "Datele exacte despre metoda de plată sunt disponibile începând cu această versiune",
                "Datele canalelor de vânzare sunt disponibile începând cu această versiune",
                "Case, comenzi online și automate de vânzare",
                "Case și comenzi online",
                "Venit din toate canalele de vânzare urmărite");

            SetCheckoutSalesChannelTerms("cs",
                "Všechny transakce", "Celkové tržby", "Pokladní transakce",
                "Online objednávky", "Prodejní automaty", "Průměrná transakce",
                "Přesná data o způsobu platby jsou dostupná od této verze",
                "Data prodejních kanálů jsou dostupná od této verze",
                "Pokladny, online objednávky a prodejní automaty",
                "Pokladny a online objednávky",
                "Tržby ze všech sledovaných prodejních kanálů");

            SetCheckoutSalesChannelTerms("lt",
                "Visos operacijos", "Bendros pajamos", "Kasos operacijos",
                "Internetiniai užsakymai", "Pardavimo automatai", "Vidutinė operacija",
                "Tikslūs mokėjimo būdo duomenys galimi nuo šios versijos",
                "Pardavimo kanalų duomenys galimi nuo šios versijos",
                "Kasos, internetiniai užsakymai ir pardavimo automatai",
                "Kasos ir internetiniai užsakymai",
                "Pajamos iš visų stebimų pardavimo kanalų");
        }

        private static void SetCheckoutAnalyticsTerms(
            string code,
            string checkouts,
            string checkoutRevenue,
            string card,
            string cash,
            string selfCheckout,
            string cashier,
            string player,
            string regularCheckout,
            string revenueSplitAvailable)
        {
            if (!Packs.TryGetValue(code, out LanguagePack pack) || pack == null)
                return;

            pack.Terms["checkouts"] = checkouts;
            pack.Terms["checkout revenue"] = checkoutRevenue;
            pack.Terms["card"] = card;
            pack.Terms["cash"] = cash;
            pack.Terms["self checkout"] = selfCheckout;
            pack.Terms["cashier"] = cashier;
            pack.Terms["player"] = player;
            pack.Terms["regular checkout"] = regularCheckout;
            pack.Terms["revenue split available from this version"] = revenueSplitAvailable;
        }

        private static void SetCheckoutSalesChannelTerms(
            string code,
            string allTransactions,
            string totalRevenue,
            string checkoutTransactions,
            string onlineOrders,
            string vendingMachines,
            string averageTransaction,
            string accuratePaymentData,
            string salesChannelData,
            string channelList,
            string channelListWithoutVending,
            string allChannelRevenue)
        {
            if (!Packs.TryGetValue(code, out LanguagePack pack) || pack == null)
                return;

            pack.Terms["all transactions"] = allTransactions;
            pack.Terms["total revenue"] = totalRevenue;
            pack.Terms["checkout transactions"] = checkoutTransactions;
            pack.Terms["online orders"] = onlineOrders;
            pack.Terms["vending machines"] = vendingMachines;
            pack.Terms["average transaction"] = averageTransaction;
            pack.Terms["accurate payment-method data available from this version"] = accuratePaymentData;
            pack.Terms["sales-channel data available from this version"] = salesChannelData;
            pack.Terms["checkouts, online orders and vending machines"] = channelList;
            pack.Terms["checkouts and online orders"] = channelListWithoutVending;
            pack.Terms["revenue from all tracked sales channels"] = allChannelRevenue;
        }
    }
}

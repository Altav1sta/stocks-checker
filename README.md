# StocksChecker
Uses <a href="https://tinkoffcreditsystems.github.io/invest-openapi/">Tinkoff API</a> for constant updating via web-socket streaming all US stocks quotes tradable in Tinkoff. 

Also gets particular stock quote info requesting <a href="">Alpaca.Markets API</a> one-by-one each 350 ms. Additionally you can subscribe for streaming updates of up to 30 selected tickers.

As a result displays a table with aggregated info by each ticker, including long/short play delta at the moment between RU and US markets that can be used for cross-border arbitrage.

<b>Important</b>:
You need to specify your Tinkoff and Alpaca API keys in appsettings.json in order to use the app.

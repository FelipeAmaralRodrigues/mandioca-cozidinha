# Mandioca Cozidinha para rinha de backend de 2025

Lapidado com o que coloca o leite na mesa das criança: 

- [.NET 9](https://dotnet.microsoft.com/download/dotnet/9.0)
- [RabbitMQ (do MassTransit)](https://hub.docker.com/r/masstransit/rabbitmq)
- [Redis](https://hub.docker.com/_/redis)
- [Nginx](https://hub.docker.com/_/nginx)

## Como é a solução?
![Diagrama da Solução](solution.png)

Resumão do resumão: fiz um semáforo que indica se pode ou não passar requisições pro default ou fallback do payment processor. Caso nenhum dos dois estejam disponíveis, joga pra fila que tem um retry de 20x, 1 a cada 1 segundo, ou seja, vai na força do ódio. Ainda vou olhar com carinho pro bonus do p99.

*mas isso pode mudar até a data de entrega, estou testando se mantenho o rabbit

## Como executar? Precisa de muito não, só mandar um enter no:
```docker-compose up -d```

E boa pa nois.
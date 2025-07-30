# Mandioca Cozidinha para rinha de backend de 2025

Lapidado com o que coloca o leite na mesa das crian�a: 

- [.NET 9](https://dotnet.microsoft.com/download/dotnet/9.0)
- [RabbitMQ (do MassTransit)](https://hub.docker.com/r/masstransit/rabbitmq)
- [Redis](https://hub.docker.com/_/redis)
- [Nginx](https://hub.docker.com/_/nginx)

## Como � a solu��o?
![Diagrama da Solu��o](solution.png)

Resum�o do resum�o: fiz um sem�foro que indica se pode ou n�o passar requisi��es pro default ou fallback do payment processor. Caso nenhum dos dois estejam dispon�veis, joga pra fila que tem um retry de 20x, 1 a cada 1 segundo, ou seja, vai na for�a do �dio. Ainda vou olhar com carinho pro bonus do p99.

*mas isso pode mudar at� a data de entrega, estou testando se mantenho o rabbit

## Como executar? Precisa de muito n�o, s� mandar um enter no:
```docker-compose up -d```

E boa pa nois.
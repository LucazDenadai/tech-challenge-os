# language: pt-BR
Funcionalidade: Coordenar uma OS distribuída por Saga
  Como oficina, quero que abertura, orçamento, pagamento e execução de uma OS sejam coordenados entre
  OS, Billing e Operações, e que um efeito já confirmado seja desfeito quando uma etapa posterior falha.
  Os três serviços rodam de verdade, com um RabbitMQ só; o pagamento usa o provedor simulado do Billing.

  Contexto:
    Dado que cliente e veículo estão aptos na filial selecionada

  # Cenário obrigatório pelo enunciado: um fluxo completo em BDD.
  @obrigatorio
  Cenário: Concluir fluxo de OS com pagamento aprovado
    Quando a OS é aberta
    E o mecânico diagnostica 2 unidades de óleo e a troca de óleo
    E o cliente aprova o orçamento
    E o Mercado Pago confirma o pagamento
    E Operações inicia e conclui o reparo consumindo 2 unidades de óleo
    Então a Saga termina no estado "Completed"
    E a OS apresenta o status "Finalizada" com o histórico do fluxo
    E Billing mantém o pagamento "Aprovado"
    E Operações mantém a execução "Concluida" e o estoque de óleo baixa 2 unidades

  Cenário: Rejeitar orçamento sem iniciar pagamento ou execução
    Quando a OS é aberta
    E o mecânico diagnostica 1 unidades de óleo e a troca de óleo
    E o cliente recusa o orçamento
    Então a Saga termina no estado "Cancelled"
    E a OS apresenta o status "Cancelada"
    E nenhuma cobrança é criada
    E Operações mantém a execução "Diagnosticada"

  Cenário: Não cobrar quando falta estoque na filial
    Quando a OS é aberta
    E o mecânico diagnostica 500 unidades de óleo e a troca de óleo
    E o cliente aprova o orçamento
    Então a Saga termina no estado "Cancelled"
    E nenhuma cobrança é criada
    E o estoque de óleo da filial não muda

  Cenário: Liberar a reserva quando o pagamento é recusado
    Quando a OS é aberta
    E o mecânico diagnostica 1 unidades de óleo e a troca de óleo
    E o cliente aprova o orçamento
    E o Mercado Pago recusa o pagamento
    Então a Saga termina no estado "Cancelled"
    E Billing mantém o pagamento "Recusado"
    E o estoque de óleo da filial não muda

  Cenário: Compensar falha operacional após pagamento aprovado
    Quando a OS é aberta
    E o mecânico diagnostica 1 unidades de óleo e a troca de óleo
    E o cliente aprova o orçamento
    E o Mercado Pago confirma o pagamento
    E Operações inicia o reparo e registra falha sem consumir peças
    Então a Saga termina no estado "Compensated"
    E a OS apresenta o status "Cancelada"
    E Billing mantém o pagamento "Estornado"
    E o estoque de óleo da filial não muda

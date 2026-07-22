# Fontes oficiais sobre Pix e gateways

Este arquivo registra fontes para decisao arquitetural. Validar novamente antes de implementar integracao real.

## Fontes

- Bacen Pix API: https://github.com/bacen/pix-api
- Bacen Swagger Pix API: https://bacen.github.io/pix-api/
- Mercado Pago Pix: https://www.mercadopago.com.br/developers/en/docs/checkout-bricks/payment-brick/payment-submission/pix
- Mercado Pago Webhooks: https://www.mercadopago.com.br/developers/en/docs/your-integrations/notifications/webhooks
- Stripe Pix: https://docs.stripe.com/payments/pix
- Pagar.me Pix: https://docs.pagar.me/docs/pix-1
- Pagar.me Webhooks: https://docs.pagar.me/reference/vis%C3%A3o-geral-sobre-webhooks

## Decisao atual

Para MVP, usar abstracao de gateway e `FakePixGateway`. Para integracao real de demo, priorizar Mercado Pago sandbox por aderencia ao Brasil e facilidade de apresentar Pix.


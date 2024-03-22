# About

Service Bus deferrals.

## Context

> Sometimes long operations can exceed lock time of the message. While renewing message locks is one of the options, another one can use message deferrals.
>
> We can defer the message while processing occurs, then complete the message after process finishes.

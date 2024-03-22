# About

Service Bus deferrals.

## Context

> We have a long process to encode data from our record files into 2 different file formats. 1 can take up to an hour or more. Due to the nature of Service bus not being able to guarantee a lock renewal, we opted to defer the message while processing occurs, then complete the message after the encode process finishes. When the message stays in context and we defer it, I assumed the lock becomes irrelevant since no other message would be read by the processor unless they have the sequence number context? Our current implementation uses a ServiceBusProcessor client and ServiceBusReceiver client to achieve this, but I was wondering if we could just remove the Receiver if the message stays in context.

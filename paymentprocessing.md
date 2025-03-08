E-Commerce Payment Processing System
Overview
This document provides a comprehensive overview of the payment processing flow in our e-commerce platform, supporting both authenticated users and guest checkout. The system is designed to be secure, robust, and provide a seamless user experience while maintaining high security standards.
Table of Contents

System Components
Payment Flow Diagram
Detailed Process Flow
Guest Checkout
Security Features
Error Handling
Data Model
Implementation Notes

System Components
The payment processing system consists of the following key components:

Frontend Components

Checkout Context (manages cart and checkout process)
Checkout Page (collects shipping information)
Payment Selection UI
Order Review UI
Success/Error Pages


Backend Components

Orders Controller
Payment Processing Service
Order/Payment Repository
Stripe Integration Service


External Services

Stripe Payment Gateway
Webhook Handler for asynchronous events



Payment Flow Diagram
Copy┌──────────────┐     ┌──────────────┐     ┌──────────────┐     ┌──────────────┐
│              │     │              │     │              │     │              │
│  Cart Page   │────►│ Checkout Page│────►│ Payment Page │────►│  Review Page │
│              │     │              │     │              │     │              │
└──────────────┘     └──────────────┘     └──────────────┘     └──────────────┘
                            │                                         │
                            │                                         │
                            ▼                                         ▼
                     ┌────────────┐                           ┌──────────────┐
                     │            │       Create Order        │              │
                     │ CheckoutAPI│◄──────────────────────────┤ Place Order  │
                     │            │                           │              │
                     └────────────┘                           └──────────────┘
                            │                                         │
                            │                                         │
                            ▼                                         │
                ┌───────────────────────┐                            │
                │                       │                             │
                │ Payment Processing    │                             │
                │ Service               │                             │
                │                       │                             │
                └───────────────────────┘                             │
                            │                                         │
                            │                                         │
                            ▼                                         │
                ┌───────────────────────┐                             │
                │                       │      Redirect to            │
                │    Stripe Checkout    │◄────────────────────────────┘
                │     Session           │
                │                       │
                └───────────────────────┘
                            │
                            │ User completes payment on Stripe
                            ▼
          ┌─────────────────────────────────┐
          │                                 │
          │   Stripe Payment Confirmation   │
          │                                 │
          └─────────────────────────────────┘
                            │
                            │
          ┌─────────────────┴─────────────────┐
          │                                   │
          ▼                                   ▼
┌──────────────────┐                 ┌──────────────────┐
│                  │                 │                  │
│ Success Redirect │                 │  Error Redirect  │
│                  │                 │                  │
└──────────────────┘                 └──────────────────┘
          │                                   │
          │                                   │
          ▼                                   ▼
┌──────────────────┐                 ┌──────────────────┐
│                  │                 │                  │
│ Verify Order API │                 │   Error Page     │
│                  │                 │                  │
└──────────────────┘                 └──────────────────┘
          │
          │
          ▼
┌──────────────────┐
│                  │
│  Success Page    │
│                  │
└──────────────────┘
Detailed Process Flow
1. Checkout Initiation
User Actions:

User adds products to cart
User proceeds to checkout
User selects checkout method (guest or authenticated)

System Actions:

CheckoutContext manages cart state and calculates totals
For guest users, displays login/create account/guest options
For authenticated users, pre-fills shipping information

2. Shipping Information Collection
User Actions:

User provides/confirms shipping information
Guest users can optionally create an account
User selects "Continue to Payment"

System Actions:

Validates all required shipping fields
If creating account, handles registration
Stores shipping information in form state

3. Payment Method Selection
User Actions:

User selects payment method (credit card, PayPal)
User selects "Continue to Review"

System Actions:

Updates payment method state
Prepares for final order review

4. Order Review and Placement
User Actions:

User reviews order details, shipping address, and payment method
User clicks "Place Order"

System Actions:

checkout() method in CheckoutContext is called
For guest users, guest information is packaged in the request
CSRF token is retrieved and included in the request
Request is sent to /api/orders/checkout

5. Order Creation (Backend)
Controller Actions:

Validates CSRF token
Validates request data and guest information if applicable
Creates a StartCheckoutRequest for the payment service
Determines user ID (authenticated user ID or "guest")

Payment Service Actions:

Generates an idempotency key to prevent duplicate orders
Validates product availability and stock
Creates order and payment records in the database
Prepares line items for Stripe

6. Stripe Session Creation
Payment Service Actions:

Creates a Stripe checkout session with:

Line items from the order
Success and cancel URLs
Customer email for guest users
Shipping information when available
Metadata for session validation


Updates the payment record with the Stripe session ID
Caches session data for later validation
Returns session ID to the client

7. Redirect to Stripe
Frontend Actions:

Redirects user to Stripe checkout page using the session ID
For guest users, stores checkout info in sessionStorage

User Actions:

Enters payment details on the Stripe-hosted page
Completes or cancels the payment

8. Payment Completion and Redirect
Stripe Actions:

Processes the payment
Redirects user to success URL with session ID (if successful)
Redirects user to cancel URL with error code (if failed/canceled)

9. Order Verification
Success Page Actions:

Fetches order details using the session ID
For guest users, includes guest email in the verification request

Verification API Actions:

Retrieves the Stripe session
Verifies payment status
Updates order and payment status
For guest users, generates an order token
Returns order details and status

10. Order Confirmation
Success Page Actions:

Displays order confirmation with details
For guest users, stores order token for future reference
Clears the shopping cart
Provides options to continue shopping or view order history

Guest Checkout
The system supports a seamless guest checkout experience with the following features:
Guest User Identification

Guest users are assigned a user ID of "guest"
Guest orders are linked to the provided email address
A secure order token is generated for future order access

Guest Information Storage

Shipping information is collected during checkout
Information is securely stored with the order
Email address is used as the primary identifier

Guest Order Tracking

Guest users receive an order token in the success response
The token is stored in sessionStorage
To access the order later, users provide their email and order number

Guest-to-Account Conversion

Guest users can create an account during checkout
Option to create an account is presented on the success page
Guest orders can be linked to newly created accounts

Security Features
The payment system implements several security measures:
CSRF Protection

CSRF tokens are required for all checkout requests
Tokens are included in a meta tag on the checkout page
Requests without a valid token are rejected

Idempotency Protection

Unique idempotency keys prevent duplicate order processing
Keys are based on user ID, cart items, and timestamp
Both our system and Stripe use these keys

Session Validation

Checkout sessions are validated on multiple levels:

Client-side validation using cached session data
Server-side validation against our database
Verification with Stripe API for payment status


Session age is checked to prevent old session reuse

Secure Tokens

Order tokens for guest users are generated using HMAC-SHA256
Tokens include a timestamp to limit validity period
Token verification requires matching against stored values

Transaction Integrity

Database transactions ensure data consistency
All critical operations use proper isolation levels
Rollback mechanisms are in place for error scenarios

Error Handling
The system provides comprehensive error handling:
Payment Failures

Stripe payment failures redirect to an error page
Error codes from Stripe are parsed and displayed
Specific recommendations are provided based on error type

System Errors

Server-side errors are logged with detailed information
Client-side error handling provides user-friendly messages
Recovery options are presented when possible

Retry Mechanisms

Users can retry failed payments
Cart contents are preserved for retry attempts
Idempotency ensures no duplicate charges

Data Model
Key entities in the payment system:
Order

Contains order details, status, and total amount
Links to order items and shipping information
For guest orders, contains basic guest details
Supports idempotency key for duplicate prevention

Payment

Records payment details, status, and methods
Links to the order and Stripe session
Tracks payment timestamps and completion status

GuestOrderInfo

Extends order information for guest users
Contains complete shipping information
Stores secure order token for access control

OrderItem

Contains product details, quantity, and pricing
Links to the parent order and product

Implementation Notes
Client-Side Storage

Cart data stored in localStorage for persistence
Guest checkout information in sessionStorage
Session IDs and tokens stored securely for verification

Database Considerations

Transactions used for data integrity
Indexes on frequently queried fields
Proper constraints for data validation

Performance Optimizations

Caching of session data for quick verification
Minimal API calls to Stripe during verification
Efficient database queries and transactions

Webhook Integration

System can be extended with Stripe webhooks
Webhooks provide asynchronous payment confirmations
Useful for handling payment disputes and refunds
# Autorization Layer

We need to build an authorization layer over the different actions we need to include decorators in each method of the controllers.

And based on the role of each user the evaluation should take place.

All the routes except the login lading page need to be signin, actually we need a landing page to login before accessing the system

## How permitions are indefied

For example we are expecting all the method cointain a decorator with the specific action.

For the soft delete action, users, roles need to be ignored only the active once should be the valid once.

example 

[VIEW_BANK]
public async Task<IActionResult> Index

## When a user has multiple roles

In the user menu, the user needs to select what roles he wants to perform in that moment.

When the user doesn't have the permition, an error page should be shown saying you don't have access to the web apge.


## Seed of the admin user.

We need to seed the admin user with a generic password.

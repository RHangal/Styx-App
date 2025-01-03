# Styx

This project was generated with [Angular CLI](https://github.com/angular/angular-cli) version 18.2.4.

## Development server

Run `ng serve` for a dev server. Navigate to `http://localhost:4200/`. The application will automatically reload if you change any of the source files.

## Code scaffolding

Run `ng generate component component-name` to generate a new component. You can also use `ng generate directive|pipe|service|class|guard|interface|enum|module`.

## Build

Run `ng build` to build the project. The build artifacts will be stored in the `dist/` directory.

## Running unit tests

Run `ng test` to execute the unit tests via [Karma](https://karma-runner.github.io).

## Running end-to-end tests

Run `ng e2e` to execute the end-to-end tests via a platform of your choice. To use this command, you need to first add a package that implements end-to-end testing capabilities.

## Further help

To get more help on the Angular CLI use `ng help` or go check out the [Angular CLI Overview and Command Reference](https://angular.dev/tools/cli) page.

## Backend

The api endpoints can be found in the styx-api folder. You can either run the functions locally or host them on Azure.

## Database

This application is setup to use CosmosDB (NoSQL) and BlobStorage in order to store user information. You'll need to configure these services through Azure if forking this repo and replace the necessary environment variables.

## Authentication

Auth0 is used to authenticate users when signing up and logging into the application. Create/Sign into your Auth0 account and create an SPA App along with an API in order to obtain the values for the Auth0 Domain and Auth0 Audience environment variables.

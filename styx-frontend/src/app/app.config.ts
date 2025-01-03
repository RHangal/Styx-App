import { ApplicationConfig, provideZoneChangeDetection } from '@angular/core';
import { provideRouter } from '@angular/router';

import { routes } from './app.routes';
import { provideAuth0 } from '@auth0/auth0-angular';
import { provideHttpClient } from '@angular/common/http';

export const appConfig: ApplicationConfig = {
  providers: [
    provideZoneChangeDetection({ eventCoalescing: true }),
    provideRouter(routes),
    provideAuth0({
      domain: 'dev-uddjm4fii5jj4jc5.us.auth0.com',
      clientId: 'VzjPNirtcOeZ2hyUDmeusr9k4LZmC9xx',
      authorizationParams: {
        redirect_uri: window.location.origin,
        audience: 'https://api.stiyx.org', // The API identifier from Auth0
      },
    }),
    provideHttpClient(),
  ],
};

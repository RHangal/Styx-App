import { Injectable } from '@angular/core';
import { AuthService } from '@auth0/auth0-angular';
import { firstValueFrom } from 'rxjs';

@Injectable({
  providedIn: 'root',
})
export class EmailAuthService {
  constructor(private auth: AuthService) {}

  async getEmailVerifiedStatus(): Promise<boolean> {
    try {
      // Convert the observable to a promise to await it
      const claims = await firstValueFrom(this.auth.idTokenClaims$);
      if (claims && typeof claims.email_verified !== 'undefined') {
        console.log('Email Verified Status:', claims.email_verified);
        return claims.email_verified;
      }
      console.error('email_verified claim is not available');
      return false;
    } catch (error) {
      console.error('Error retrieving claims:', error);
      return false;
    }
  }
}

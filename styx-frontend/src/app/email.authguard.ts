import { Injectable } from '@angular/core';
import { CanActivate, Router } from '@angular/router';
import { EmailAuthService } from './email.authservice';

//Authgaurd to check if user has verified their email
@Injectable({
  providedIn: 'root',
})
export class EmailAuthGuard implements CanActivate {
  constructor(private auth: EmailAuthService, private router: Router) {}

  async canActivate(): Promise<boolean> {
    const emailVerified = await this.auth.getEmailVerifiedStatus();

    if (emailVerified) {
      return true;
    } else {
      this.router.navigate(['/unverified']);
      return false;
    }
  }
}

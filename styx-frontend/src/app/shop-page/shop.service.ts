import { Injectable } from '@angular/core';
import { HttpClient, HttpHeaders } from '@angular/common/http';
import { AuthService } from '@auth0/auth0-angular';
import { Observable } from 'rxjs';
import { switchMap } from 'rxjs/operators';
import { environment } from '../../environment';

@Injectable({
  providedIn: 'root',
})
export class ShopService {
  private baseUrl = environment.apiUrl;

  constructor(
    private http: HttpClient,
    private auth: AuthService // from '@auth0/auth0-angular'
  ) {}

  /**
   * Fetch the list of badges (if your endpoint is public, no token needed).
   * If the backend requires a token for reading badges, you can wrap this with getAccessTokenSilently as well.
   */
  getBadges(): Observable<any> {
    return this.http.get<any>(`${this.baseUrl}badges`);
  }

  /**
   * Purchase a badge, removing the need for Auth0UserId in the request body.
   * The backend extracts user ID from the token's 'sub' claim.
   */
  purchaseBadges(Value: number, ImageUrl: string): Observable<any> {
    return this.auth.getAccessTokenSilently().pipe(
      switchMap((token: string) => {
        const headers = new HttpHeaders()
          .set('Authorization', `Bearer ${token}`)
          .set('Content-Type', 'application/json');

        // The body no longer contains Auth0UserId
        const body = { Value, ImageUrl };
        console.log('Purchasing badge with payload:', body);

        return this.http.post<any>(
          `${this.baseUrl}profile/purchase-badges`,
          body,
          { headers }
        );
      })
    );
  }

  /**
   * If your backend user profile route is now token-based (like the others),
   * remove 'auth0UserId' from the path. Otherwise, here's the old approach:
   */
  getUserProfile(): Observable<any> {
    return this.auth.getAccessTokenSilently().pipe(
      switchMap((token: string) => {
        const headers = new HttpHeaders().set(
          'Authorization',
          `Bearer ${token}`
        );
        return this.http.get<any>(`${this.baseUrl}profile`, { headers });
      })
    );
  }
}

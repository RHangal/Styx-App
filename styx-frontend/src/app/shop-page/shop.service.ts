import { Injectable } from '@angular/core';
import { HttpClient, HttpHeaders } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../environment';

@Injectable({
  providedIn: 'root',
})
export class ShopService {
  private baseUrl = environment.apiUrl;

  constructor(private http: HttpClient) {}

  // Fetch badges
  getBadges(): Observable<any> {
    return this.http.get<any>(`${this.baseUrl}badges`);
  }

  purchaseBadges(
    Auth0UserId: string,
    stringValue: number,
    ImageUrl: string
  ): Observable<any> {
    const Value = Number(stringValue);
    const body = { Auth0UserId, Value, ImageUrl };
    console.log(body);
    return this.http.post<any>(`${this.baseUrl}profile/purchase-badges`, body, {
      headers: new HttpHeaders({ 'Content-Type': 'application/json' }),
    });
  }
  // Fetch profile by Auth0 user ID
  getUserProfile(auth0UserId: string): Observable<any> {
    return this.http.get<any>(`${this.baseUrl}profile/${auth0UserId}`);
  }
}

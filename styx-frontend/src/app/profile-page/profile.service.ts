import { Injectable } from '@angular/core';
import { HttpClient, HttpHeaders } from '@angular/common/http';
import { AuthService } from '@auth0/auth0-angular';
import { Observable } from 'rxjs';
import { switchMap } from 'rxjs/operators';
import { environment } from '../../environment';

@Injectable({
  providedIn: 'root',
})
export class ProfileService {
  private baseUrl = environment.apiUrl; // e.g., "http://localhost:7071/api/"

  constructor(private http: HttpClient, private auth: AuthService) {}

  /**
   * 1) getUserProfile
   * The backend identifies the user by the token's 'sub' (Auth0 user ID).
   */
  getUserProfile(): Observable<any> {
    return this.auth.getAccessTokenSilently().pipe(
      switchMap((token: string) => {
        const headers = new HttpHeaders().set('Authorization', `Bearer ${token}`);
        return this.http.get<any>(`${this.baseUrl}profile`, { headers });
      })
    );
  }

  /**
   * 2) updateUserProfile
   * The backend identifies the user by 'sub'. We only send the data to update.
   */
  updateUserProfile(profileData: any): Observable<any> {
    return this.auth.getAccessTokenSilently().pipe(
      switchMap((token: string) => {
        const headers = new HttpHeaders()
          .set('Authorization', `Bearer ${token}`)
          .set('Content-Type', 'application/json');

        return this.http.put<any>(`${this.baseUrl}profile`, profileData, { headers });
      })
    );
  }

  /**
   * 3) uploadMedia
   * Uploads a file to the server, which reads the user from the token's 'sub'.
   */
  uploadMedia(file: File): Observable<any> {
    return this.auth.getAccessTokenSilently().pipe(
      switchMap((token: string) => {
        const formData = new FormData();
        formData.append('file', file);
        // If your backend supports an optional 'profile' field for subfolders
        formData.append('profile', 'pfp');

        const headers = new HttpHeaders().set('Authorization', `Bearer ${token}`);

        return this.http.post<any>(`${this.baseUrl}media/upload`, formData, { headers });
      })
    );
  }

  /**
   * 4) updateProfilePhoto
   * Updates the user's photo, with the backend extracting the 'sub' from the token.
   */
  updateProfilePhoto(photoUrl: string): Observable<any> {
    return this.auth.getAccessTokenSilently().pipe(
      switchMap((token: string) => {
        const headers = new HttpHeaders()
          .set('Authorization', `Bearer ${token}`)
          .set('Content-Type', 'application/json');

        const body = { photoUrl };
        return this.http.put<any>(`${this.baseUrl}profile/media`, body, { headers });
      })
    );
  }

  /**
   * 5) uploadNewProfilePhoto
   * High-level approach:
   *  1) uploadMedia to get the file URL
   *  2) call updateProfilePhoto with that URL
   */
  uploadNewProfilePhoto(file: File): Observable<any> {
    // "Chaining" Observables using switchMap
    return this.uploadMedia(file).pipe(
      switchMap((mediaResponse) => {
        const photoUrl = mediaResponse.url; // The file URL from the backend
        return this.updateProfilePhoto(photoUrl);
      })
    );
  }
}

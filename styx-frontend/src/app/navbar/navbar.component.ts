import { DOCUMENT } from '@angular/common';
import { ChangeDetectorRef, Component, Inject } from '@angular/core';
import { RouterModule } from '@angular/router';
import { AuthService } from '@auth0/auth0-angular';
import { CommonModule } from '@angular/common';
import { NavbarService } from '../navbar.service';

@Component({
  selector: 'app-navbar',
  imports: [RouterModule, CommonModule],
  standalone: true,
  templateUrl: './navbar.component.html',
  styleUrls: ['./navbar.component.sass'],
})
export class NavbarComponent {
  constructor(
    @Inject(DOCUMENT) public document: Document,
    public auth: AuthService,
    private changeDetectorRef: ChangeDetectorRef,
    private navbarService: NavbarService // Inject the shared service
  ) {}
  profilePhotoUrl: string | null = null;

  ngOnInit(): void {
    // Listen for navbar refresh triggers
    this.navbarService.refreshNavbar$.subscribe(() => {
      this.loadProfilePhoto();
      this.changeDetectorRef.detectChanges(); // Force re-render
    });
    this.loadProfilePhoto();
  }

  loadProfilePhoto(): void {
    this.profilePhotoUrl = localStorage.getItem('pfp');
  }

  logout(): void {
    // Clear the profile photo from localStorage
    localStorage.removeItem('pfp');

    // Perform the Auth0 logout
    this.auth.logout({
      logoutParams: { returnTo: document.location.origin },
    });
  }
}
